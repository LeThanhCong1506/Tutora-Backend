using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Helpers;
using MV.DomainLayer.Utilities;
using System.Globalization;
using System.Text.Json;

namespace MV.ApplicationLayer.Services
{
    public partial class TutorService
    {
        private const double CccdNameMatchThreshold = 0.80;

        // ─── Media Methods (avatar + video) ─────────────────────────────────

        public async Task<string?> UpdateTutorAvatarAsync(string userId, IFormFile avatarFile)
        {
            if (avatarFile == null || avatarFile.Length == 0)
                throw new ArgumentException("Vui lòng chọn ảnh đại diện");

            ValidateImageFile(avatarFile);

            var user = await _unitOfWork.UserRepository.GetUserByIdAsync(userId);
            if (user == null) return null;

            var avatarUrl = await _storageService.UploadFileAsync(AvatarBucket, userId, avatarFile);
            user.Avatarurl = avatarUrl;

            await _unitOfWork.SaveChangesAsync();
            return avatarUrl;
        }

        public async Task<bool> UpdateTutorVideoAsync(string userId, UpdateTutorVideoRequest request)
        {
            var profile = await _unitOfWork.TutorRepository.GetTutorProfileByIdAsync(userId);
            if (profile == null) return false;

            if (!IsValidYoutubeUrl(request.VideoUrl))
                throw new ArgumentException("Vui lòng nhập link video YouTube hợp lệ.");

            profile.Videointrourl = request.VideoUrl.Trim();
            profile.Updatedat = TimeZoneHelper.UtcNow;

            await _unitOfWork.SaveChangesAsync();
            return true;
        }

        private static bool IsValidYoutubeUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            return System.Text.RegularExpressions.Regex.IsMatch(
                url,
                @"^(https?://)?(www\.|m\.)?(youtube\.com/(watch\?v=|shorts/|embed/)|youtu\.be/)[\w\-]{11}",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        // ─── CCCD Upload ─────────────────────────────────────────────────────

        public async Task<CccdUploadResponse> UploadCccdImagesAsync(string userId, UploadCccdRequest request)
        {
            ValidateCccdImageFile(request.FrontImage, "mặt trước");
            ValidateCccdImageFile(request.BackImage, "mặt sau");

            var user = await _unitOfWork.UserRepository.GetUserByIdAsync(userId)
                ?? throw new ArgumentException("Không tìm thấy người dùng.");

            // 1. Đọc bytes mặt trước một lần dùng cho cả FPT.AI và Cloudinary
            byte[] frontBytes;
            using (var ms = new MemoryStream())
            {
                await request.FrontImage.CopyToAsync(ms);
                frontBytes = ms.ToArray();
            }

            // 2. Gọi FPT.AI OCR trực tiếp bằng bytes — không qua URL, không lộ ảnh
            var ocrResult = await RunFptAiOcrAsync(frontBytes, request.FrontImage.FileName);

            if (ocrResult != null)
            {
                // 3. Kiểm tra độ tin cậy OCR (< 90% → ảnh mờ/giả)
                if (ocrResult.Probability < 90.0)
                    throw new InvalidOperationException(
                        $"Ảnh CCCD không đủ rõ nét hoặc có dấu hiệu giả mạo (độ tin cậy: {ocrResult.Probability:F1}%). Vui lòng chụp lại.");

                // 4. Validate tên CCCD vs tên hồ sơ bằng Fuzzy Matching (Levenshtein)
                if (!string.IsNullOrWhiteSpace(ocrResult.Name) && !string.IsNullOrWhiteSpace(user.Fullname))
                {
                    var (isMatch, similarity) = StringSimilarity.CompareNames(ocrResult.Name, user.Fullname, CccdNameMatchThreshold);
                    _logger.LogInformation(
                        "CCCD name match for user {UserId}: OCR='{OcrName}' Profile='{ProfileName}' Similarity={Sim:F2} Match={Match}",
                        userId, ocrResult.Name, user.Fullname, similarity, isMatch);

                    if (!isMatch)
                        throw new InvalidOperationException(
                            $"Họ và tên trên CCCD \"{ocrResult.Name}\" không khớp với hồ sơ \"{user.Fullname}\" " +
                            $"(độ tương đồng: {similarity:P0}). Vui lòng cập nhật đúng họ tên trước khi xác minh CCCD.");
                }

                // 5. Kiểm tra số CCCD trùng với tài khoản khác
                if (!string.IsNullOrEmpty(ocrResult.Id) && ocrResult.Id != _encryption.Decrypt(user.Identitynumber))
                {
                    var isUnique = await _unitOfWork.UserRepository.IsIdentityNumberUniqueAsync(_encryption.Encrypt(ocrResult.Id));
                    if (!isUnique)
                        throw new InvalidOperationException(
                            "Số CCCD này đã được xác minh bởi tài khoản khác. Vui lòng liên hệ hỗ trợ nếu đây là nhầm lẫn.");
                }
            }

            // 6. Upload Cloudinary private → lưu PublicId (không có chữ ký, không thể truy cập trực tiếp)
            var frontPublicId = await _storageService.UploadPrivateFileAsync(
                CccdBucket, userId + "/front",
                CreateFormFileFromBytes(frontBytes, request.FrontImage.FileName, request.FrontImage.ContentType));
            var backPublicId = await _storageService.UploadPrivateFileAsync(
                CccdBucket, userId + "/back", request.BackImage);

            user.Idcardfronturl = frontPublicId;
            user.Idcardbackurl  = backPublicId;

            if (ocrResult != null)
            {
                // 7. Lưu số CCCD + raw OCR data (AES-256-CBC encrypted)
                user.Identitynumber = _encryption.Encrypt(ocrResult.Id);
                user.Ekycrawdata    = _encryption.Encrypt(JsonSerializer.Serialize(new
                {
                    OcrResult = new { id = ocrResult.Id, name = ocrResult.Name, dob = ocrResult.Dob, sex = ocrResult.Sex, address = ocrResult.Address },
                    VerifiedAt = TimeZoneHelper.UtcNow.ToString("o")
                }));

                // 8. Mark identity verified nếu OCR đạt ngưỡng tin cậy
                user.Isidentityverified = ocrResult.Probability >= 90.0;

                // 9. Auto-fill thông tin cá nhân nếu user chưa điền
                if (string.IsNullOrWhiteSpace(user.Fullname))
                    user.Fullname = ocrResult.Name;

                if (string.IsNullOrWhiteSpace(user.Address))
                    user.Address = ocrResult.Address;

                if (user.Gender == null)
                    user.Gender = GenderHelper.FromEkycSex(ocrResult.Sex);

                if (user.Birthdate == null && !string.IsNullOrWhiteSpace(ocrResult.Dob) &&
                    DateOnly.TryParseExact(ocrResult.Dob, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dob))
                    user.Birthdate = dob;
            }

            await _unitOfWork.UserRepository.UpdateUserAsync(user);
            await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation("CCCD uploaded for user {UserId}, OCR={OcrSuccess}, Verified={Verified}",
                userId, ocrResult != null, user.Isidentityverified);

            if (user.Isidentityverified == true)
                await AutoSubmitIfCompleteAsync(userId);

            return new CccdUploadResponse
            {
                OcrSuccess     = ocrResult != null,
                IdentityNumber = MaskIdentityNumber(ocrResult?.Id),
                FullName       = ocrResult?.Name,
                DateOfBirth    = ocrResult?.Dob,
                Gender         = ocrResult?.Sex,
                Address        = ocrResult?.Address,
                Message        = ocrResult != null
                    ? "Upload và đọc CCCD thành công."
                    : "Upload thành công. Không đọc được thông tin CCCD, Admin sẽ xác minh thủ công."
            };
        }

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

        private static IFormFile CreateFormFileFromBytes(byte[] bytes, string fileName, string contentType)
        {
            var stream = new MemoryStream(bytes);
            return new FormFile(stream, 0, bytes.Length, "file", fileName)
            {
                Headers     = new HeaderDictionary(),
                ContentType = contentType
            };
        }

        // ─── Private helpers ─────────────────────────────────────────────────

        private static void ValidateImageFile(IFormFile file)
        {
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png" };
            if (!allowedExtensions.Contains(Path.GetExtension(file.FileName).ToLowerInvariant()))
                throw new ArgumentException("Chỉ chấp nhận ảnh JPG và PNG cho ảnh đại diện");
            if (file.Length > 5 * 1024 * 1024)
                throw new ArgumentException("Ảnh đại diện phải nhỏ hơn 5MB");
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
