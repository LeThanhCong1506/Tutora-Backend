using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.RepositoryInterfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Helpers;

namespace MV.ApplicationLayer.Services
{
    /// <summary>
    /// CHỈ dùng ngày sinh để chứng minh đủ 16 tuổi, KHÔNG lưu ảnh.
    /// Có lưu số CCCD (mã hóa AES) DUY NHẤT để chống 1 CCCD dùng cho nhiều tài khoản né gate tuổi.
    /// </summary>
    public class StudentIdentityService : IStudentIdentityService
    {
        private const double MinConfidence = 90.0;       // < 90% → ảnh mờ/giả

        private readonly IFptAiService _fptAiService;
        private readonly IEncryptionService _encryption;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<StudentIdentityService> _logger;

        public StudentIdentityService(
            IFptAiService fptAiService,
            IEncryptionService encryption,
            IUnitOfWork unitOfWork,
            ILogger<StudentIdentityService> logger)
        {
            _fptAiService = fptAiService ?? throw new ArgumentNullException(nameof(fptAiService));
            _encryption = encryption ?? throw new ArgumentNullException(nameof(encryption));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<EkycVerificationResult> VerifyAndApplyAsync(User user, UploadCccdRequest request, int minAgeRequired)
        {
            ValidateCccdImageFile(request.FrontImage, "mặt trước");
            ValidateCccdImageFile(request.BackImage, "mặt sau");

            // 1. Đọc bytes mặt trước → gửi trực tiếp cho FPT.AI OCR (không lưu ảnh, không qua URL).
            byte[] frontBytes;
            using (var ms = new MemoryStream())
            {
                await request.FrontImage.CopyToAsync(ms);
                frontBytes = ms.ToArray();
            }

            var ocrResult = await RunFptAiOcrAsync(frontBytes, request.FrontImage.FileName);

            // 2. Học sinh BẮT BUỘC đọc được CCCD (cần DOB để chứng minh tuổi).
            if (ocrResult == null)
                throw new InvalidOperationException(
                    "Không đọc được thông tin trên CCCD. Vui lòng chụp lại rõ nét hơn.");

            // 3. Độ tin cậy OCR.
            if (ocrResult.Probability < MinConfidence)
                throw new InvalidOperationException(
                    $"Ảnh CCCD không đủ rõ nét hoặc có dấu hiệu giả mạo. Vui lòng chụp lại.");

            // 4. CHỈ lấy NGÀY SINH để xác minh độ tuổi. KHÔNG check tên, KHÔNG lưu ảnh/raw eKYC.
            DateOnly? dob = null;
            if (!string.IsNullOrWhiteSpace(ocrResult.Dob) &&
                DateOnly.TryParseExact(ocrResult.Dob, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDob))
                dob = parsedDob;

            if (dob == null)
                throw new InvalidOperationException(
                    "Không đọc được ngày sinh trên CCCD. Vui lòng chụp lại rõ nét hơn.");

            var age = AgeHelper.CalculateAge(dob.Value);
            if (age < minAgeRequired)
                throw new InvalidOperationException(
                    $"Bạn chưa đủ {minAgeRequired} tuổi nên chưa thể đặt lịch học.");

            // 5. Chống trùng: 1 số CCCD không được xác minh cho nhiều tài khoản khác nhau
            //    Dùng AES deterministic (giống tutor) để so khớp.
            var encryptedId = _encryption.Encrypt(ocrResult.Id);
            if (!string.IsNullOrEmpty(ocrResult.Id) && encryptedId != user.Identitynumber)
            {
                var isUnique = await _unitOfWork.UserRepository.IsIdentityNumberUniqueAsync(encryptedId);
                if (!isUnique)
                    throw new InvalidOperationException(
                        "CCCD này đã được sử dụng bởi tài khoản khác. Vui lòng liên hệ hỗ trợ nếu đây là nhầm lẫn.");
            }

            // 6. Đủ tuổi + CCCD hợp lệ → đánh dấu đã xác minh, ghi ngày sinh
            user.Isidentityverified = true;
            user.Birthdate = dob;
            user.Identitynumber = encryptedId;

            return new EkycVerificationResult
            {
                Ocr = ocrResult,
                DateOfBirth = dob,
                Verified = true,
                Response = new CccdUploadResponse
                {
                    OcrSuccess = true,
                    DateOfBirth = ocrResult.Dob,
                    Message = "Xác minh độ tuổi thành công."
                }
            };
        }

        // Private helpers

        private async Task<FptAiResult?> RunFptAiOcrAsync(byte[] imageBytes, string fileName)
        {
            try
            {
                using var stream = new MemoryStream(imageBytes);
                var response = await _fptAiService.VerifyIdCardAsync(stream, fileName);
                return response?.Data?.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FPT.AI OCR failed for student verification.");
                return null;
            }
        }

        private static void ValidateCccdImageFile(IFormFile file, string side)
        {
            if (file == null || file.Length == 0)
                throw new ArgumentException($"Ảnh CCCD {side} không được để trống.");
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png" };
            if (!allowedExtensions.Contains(Path.GetExtension(file.FileName).ToLowerInvariant()))
                throw new ArgumentException($"Ảnh CCCD {side} chỉ chấp nhận định dạng JPG, JPEG hoặc PNG.");
            if (file.Length > 5 * 1024 * 1024)
                throw new ArgumentException($"Ảnh CCCD {side} phải nhỏ hơn 5MB.");
        }
    }
}
