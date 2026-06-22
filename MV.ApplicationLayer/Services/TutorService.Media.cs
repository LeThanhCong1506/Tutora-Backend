using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;

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

        /// <summary>Upload CCCD front and back images, save URLs to user profile.</summary>
        public async Task<CccdUploadResponse> UploadCccdImagesAsync(string userId, UploadCccdRequest request)
        {
            ValidateCccdImageFile(request.FrontImage, "mặt trước");
            ValidateCccdImageFile(request.BackImage, "mặt sau");

            var user = await _unitOfWork.UserRepository.GetUserByIdAsync(userId)
                ?? throw new ArgumentException("Không tìm thấy người dùng.");

            // CCCD là tài liệu nhạy cảm → upload private, không public
            var frontUrl = await _storageService.UploadPrivateFileAsync(CccdBucket, userId + "/front", request.FrontImage);
            var backUrl  = await _storageService.UploadPrivateFileAsync(CccdBucket, userId + "/back",  request.BackImage);

            user.Idcardfronturl = frontUrl;
            user.Idcardbackurl  = backUrl;

            await _unitOfWork.UserRepository.UpdateUserAsync(user);
            await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation("CCCD uploaded for user {UserId}: front={Front}, back={Back}", userId, frontUrl, backUrl);

            return new CccdUploadResponse
            {
                FrontImageUrl = frontUrl,
                BackImageUrl  = backUrl
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
