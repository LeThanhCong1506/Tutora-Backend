using System.Globalization;
using System.Text.Json;
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
using MV.DomainLayer.Utilities;

namespace MV.ApplicationLayer.Services
{
    /// <summary>
    /// Xác minh CCCD/eKYC dùng chung (FPT.AI OCR). Trích xuất từ luồng tutor để tutor và học sinh
    /// dùng chung một nguồn logic duy nhất.
    /// </summary>
    public class EkycService : IEkycService
    {
        private const double MinConfidence = 90.0;       // < 90% → ảnh mờ/giả
        private const double NameMatchThreshold = 0.80;  // ngưỡng khớp tên (fuzzy)

        private readonly IFptAiService _fptAiService;
        private readonly IEncryptionService _encryption;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<EkycService> _logger;

        public EkycService(
            IFptAiService fptAiService,
            IEncryptionService encryption,
            IUnitOfWork unitOfWork,
            ILogger<EkycService> logger)
        {
            _fptAiService = fptAiService ?? throw new ArgumentNullException(nameof(fptAiService));
            _encryption = encryption ?? throw new ArgumentNullException(nameof(encryption));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<EkycVerificationResult> VerifyAndApplyAsync(User user, UploadCccdRequest request, EkycVerificationOptions options)
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

            // 2. Bắt buộc đọc được CCCD (nếu yêu cầu).
            if (ocrResult == null && options.RequireOcr)
                throw new InvalidOperationException(
                    "Không đọc được thông tin trên CCCD. Vui lòng chụp lại rõ nét hơn.");

            DateOnly? dob = null;

            if (ocrResult != null)
            {
                // 3. Độ tin cậy OCR.
                if (ocrResult.Probability < MinConfidence)
                    throw new InvalidOperationException(
                        $"Ảnh CCCD không đủ rõ nét hoặc có dấu hiệu giả mạo. Vui lòng chụp lại.");

                // 4. Kiểm tra độ tuổi (nếu yêu cầu) — phải parse được DOB và đủ tuổi mới cho xác minh.
                if (!string.IsNullOrWhiteSpace(ocrResult.Dob) &&
                    DateOnly.TryParseExact(ocrResult.Dob, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDob))
                    dob = parsedDob;

                if (options.MinAgeRequired.HasValue)
                {
                    if (dob == null)
                        throw new InvalidOperationException(
                            "Không đọc được ngày sinh trên CCCD. Vui lòng chụp lại rõ nét hơn.");

                    var age = AgeHelper.CalculateAge(dob.Value);
                    if (age < options.MinAgeRequired.Value)
                        throw new InvalidOperationException(
                            $"Bạn chưa đủ {options.MinAgeRequired.Value} tuổi nên chưa thể xác minh CCCD để đặt lịch học.");
                }

                // 5. Khớp tên trên CCCD với hồ sơ (nếu cả hai đều có) bằng Fuzzy Matching.
                if (!string.IsNullOrWhiteSpace(ocrResult.Name) && !string.IsNullOrWhiteSpace(user.Fullname))
                {
                    var (isMatch, similarity) = StringSimilarity.CompareNames(ocrResult.Name, user.Fullname, NameMatchThreshold);
                    _logger.LogInformation(
                        "CCCD name match for user {UserId}: OCR='{OcrName}' Profile='{ProfileName}' Similarity={Sim:F2} Match={Match}",
                        user.Userid, ocrResult.Name, user.Fullname, similarity, isMatch);

                    if (!isMatch)
                        throw new InvalidOperationException(
                            $"Họ và tên trên CCCD \"{ocrResult.Name}\" không khớp với hồ sơ \"{user.Fullname}\". Vui lòng cập nhật đúng họ tên trước khi xác minh CCCD.");
                }

                // 6. Số CCCD không được trùng với tài khoản khác.
                if (!string.IsNullOrEmpty(ocrResult.Id) && ocrResult.Id != _encryption.Decrypt(user.Identitynumber))
                {
                    var isUnique = await _unitOfWork.UserRepository.IsIdentityNumberUniqueAsync(_encryption.Encrypt(ocrResult.Id));
                    if (!isUnique)
                        throw new InvalidOperationException(
                            "CCCD này đã được xác minh bởi tài khoản khác. Vui lòng liên hệ hỗ trợ nếu đây là nhầm lẫn.");
                }
            }

            // 7. Không lưu ảnh — chỉ giữ dữ liệu đã mã hóa (AES-256).
            user.Idcardfronturl = null;
            user.Idcardbackurl = null;

            var verified = false;
            if (ocrResult != null)
            {
                user.Identitynumber = _encryption.Encrypt(ocrResult.Id);
                user.Ekycrawdata = _encryption.Encrypt(JsonSerializer.Serialize(new
                {
                    OcrResult = new { id = ocrResult.Id, name = ocrResult.Name, dob = ocrResult.Dob, sex = ocrResult.Sex, address = ocrResult.Address },
                    VerifiedAt = TimeZoneHelper.UtcNow.ToString("o")
                }));

                verified = ocrResult.Probability >= MinConfidence;
                user.Isidentityverified = verified;

                if (options.AutoFillProfile)
                {
                    if (string.IsNullOrWhiteSpace(user.Fullname))
                        user.Fullname = ocrResult.Name;
                    if (string.IsNullOrWhiteSpace(user.Address))
                        user.Address = ocrResult.Address;
                    if (user.Gender == null)
                        user.Gender = GenderHelper.FromEkycSex(ocrResult.Sex);
                    // Với luồng gate độ tuổi (học sinh), DOB trên CCCD là nguồn chuẩn → ghi đè.
                    // Các luồng khác (tutor) chỉ điền khi còn trống.
                    if (dob != null && (user.Birthdate == null || options.MinAgeRequired.HasValue))
                        user.Birthdate = dob;
                }
            }

            return new EkycVerificationResult
            {
                Ocr = ocrResult,
                DateOfBirth = dob,
                Verified = verified,
                Response = new CccdUploadResponse
                {
                    OcrSuccess = ocrResult != null,
                    IdentityNumber = MaskIdentityNumber(ocrResult?.Id),
                    FullName = ocrResult?.Name,
                    DateOfBirth = ocrResult?.Dob,
                    Gender = ocrResult?.Sex,
                    Address = ocrResult?.Address,
                    Message = ocrResult != null
                        ? "Upload và đọc CCCD thành công."
                        : "Upload thành công. Không đọc được thông tin CCCD, Admin sẽ xác minh thủ công."
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
                _logger.LogWarning(ex, "FPT.AI OCR failed, proceeding without OCR data.");
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

        private static string? MaskIdentityNumber(string? number)
        {
            if (string.IsNullOrWhiteSpace(number) || number.Length < 6) return null;
            var visible = Math.Min(3, number.Length);
            var tail = Math.Min(4, number.Length - visible);
            var masked = new string('*', number.Length - visible - tail);
            return number[..visible] + masked + number[^tail..];
        }
    }
}
