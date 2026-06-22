using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Helpers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MV.ApplicationLayer.Services
{
    public partial class TutorService
    {
        // ─── Media Methods (avatar + video) ─────────────────────────────────

        /// <summary>Upload/Update tutor avatar.</summary>
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
            profile.Updatedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

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

        /// <summary>
        /// Upload CCCD, gọi FPT.AI OCR bằng bytes trực tiếp (không qua URL),
        /// sau đó upload Cloudinary dạng private và lưu PublicId vào DB.
        /// </summary>
        public async Task<CccdUploadResponse> UploadCccdImagesAsync(string userId, UploadCccdRequest request)
        {
            ValidateCccdImageFile(request.FrontImage, "mặt trước");
            ValidateCccdImageFile(request.BackImage, "mặt sau");

            var user = await _unitOfWork.UserRepository.GetUserByIdAsync(userId)
                ?? throw new ArgumentException("Không tìm thấy người dùng.");

            // 1. Đọc bytes mặt trước một lần, dùng cho cả FPT.AI và Cloudinary
            byte[] frontBytes;
            using (var ms = new MemoryStream())
            {
                await request.FrontImage.CopyToAsync(ms);
                frontBytes = ms.ToArray();
            }

            // 2. Gọi FPT.AI OCR với bytes — không cần URL, không lộ ảnh ra ngoài
            var ocrResult = await RunFptAiOcrAsync(frontBytes, request.FrontImage.FileName);

            // 3. Validate tên CCCD vs tên hồ sơ (fail fast trước khi upload Cloudinary)
            if (ocrResult != null && !string.IsNullOrWhiteSpace(ocrResult.Name)
                                  && !string.IsNullOrWhiteSpace(user.Fullname))
            {
                var profileName = NormalizeVietnameseName(user.Fullname);
                var cccdName    = NormalizeVietnameseName(ocrResult.Name);

                if (profileName != cccdName)
                    throw new InvalidOperationException(
                        $"Họ và tên trên CCCD \"{ocrResult.Name}\" không khớp với hồ sơ \"{user.Fullname}\". " +
                        "Vui lòng cập nhật đúng họ tên trước khi xác minh CCCD.");
            }

            // 4. Upload Cloudinary private (song song 2 ảnh) → lưu PublicId (không có chữ ký)
            var frontPublicId = await _storageService.UploadPrivateFileAsync(
                CccdBucket, userId + "/front", CreateFormFileFromBytes(frontBytes, request.FrontImage.FileName, request.FrontImage.ContentType));
            var backPublicId = await _storageService.UploadPrivateFileAsync(
                CccdBucket, userId + "/back", request.BackImage);

            user.Idcardfronturl = frontPublicId;
            user.Idcardbackurl  = backPublicId;

            // 5. Lưu kết quả OCR vào EkycRawData + Identitynumber nếu đọc được
            if (ocrResult != null)
            {
                var ekycPayload = new
                {
                    OcrResult = new
                    {
                        id      = ocrResult.Id,
                        name    = ocrResult.Name,
                        dob     = ocrResult.Dob,
                        sex     = ocrResult.Sex,
                        address = ocrResult.Address
                    },
                    VerifiedAt = TimeZoneHelper.UtcNow.ToString("o")
                };
                user.Ekycrawdata     = JsonSerializer.Serialize(ekycPayload);
                user.Identitynumber  = ocrResult.Id; // Lưu số CCCD vào cột riêng
            }

            await _unitOfWork.UserRepository.UpdateUserAsync(user);
            await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation("CCCD uploaded for user {UserId}, OCR success={OcrSuccess}", userId, ocrResult != null);

            return new CccdUploadResponse
            {
                OcrSuccess      = ocrResult != null,
                IdentityNumber  = ocrResult?.Id,
                FullName        = ocrResult?.Name,
                DateOfBirth     = ocrResult?.Dob,
                Gender          = ocrResult?.Sex,
                Address         = ocrResult?.Address,
                Message         = ocrResult != null
                    ? "Upload và đọc CCCD thành công."
                    : "Upload thành công. Không đọc được thông tin CCCD, Admin sẽ xác minh thủ công."
            };
        }

        private async Task<MV.DomainLayer.DTO.ResponseModel.FptAiResult?> RunFptAiOcrAsync(byte[] imageBytes, string fileName)
        {
            try
            {
                using var stream = new MemoryStream(imageBytes);
                var response = await _fptAiService.VerifyIdCardAsync(stream, fileName);
                return response?.Data?.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FPT.AI OCR failed for CCCD, proceeding without OCR data.");
                return null;
            }
        }

        // Tạo IFormFile tạm từ byte[] để truyền vào UploadPrivateFileAsync
        private static IFormFile CreateFormFileFromBytes(byte[] bytes, string fileName, string contentType)
        {
            var stream = new MemoryStream(bytes);
            return new FormFile(stream, 0, bytes.Length, "file", fileName)
            {
                Headers = new HeaderDictionary(),
                ContentType = contentType
            };
        }

        // ─── Private helpers ─────────────────────────────────────────────────

        private static void ValidateImageFile(IFormFile file)
        {
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png" };
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

            if (!allowedExtensions.Contains(extension))
                throw new ArgumentException("Chỉ chấp nhận ảnh JPG và PNG cho ảnh đại diện");

            if (file.Length > 5 * 1024 * 1024)
                throw new ArgumentException("Ảnh đại diện phải nhỏ hơn 5MB");
        }

        // Chuẩn hoá tên tiếng Việt để so sánh: bỏ dấu, lowercase, bỏ khoảng trắng thừa
        private static string NormalizeVietnameseName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;

            // Xử lý ký tự đặc biệt không decompose được qua NFD
            name = name.ToLowerInvariant()
                       .Replace("đ", "d");

            // Decompose diacritics rồi loại NonSpacingMark (dấu thanh, dấu mũ...)
            var normalized = name.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (var c in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }

            // Bỏ khoảng trắng thừa giữa các từ
            return Regex.Replace(sb.ToString().Trim(), @"\s+", " ");
        }

        private static void ValidateCccdImageFile(IFormFile file, string side)
        {
            if (file == null || file.Length == 0)
                throw new ArgumentException($"Ảnh CCCD {side} không được để trống.");

            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png" };
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

            if (!allowedExtensions.Contains(extension))
                throw new ArgumentException($"Ảnh CCCD {side} chỉ chấp nhận định dạng JPG, JPEG hoặc PNG.");

            if (file.Length > 5 * 1024 * 1024)
                throw new ArgumentException($"Ảnh CCCD {side} phải nhỏ hơn 5MB.");
        }
    }
}
