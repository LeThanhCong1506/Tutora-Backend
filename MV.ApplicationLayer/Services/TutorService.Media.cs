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
        public async Task<bool> UpdateTutorAvatarAsync(string userId, IFormFile avatarFile)
        {
            if (avatarFile == null || avatarFile.Length == 0)
                throw new ArgumentException("Vui lòng chọn ảnh đại diện");

            ValidateImageFile(avatarFile);

            var user = await _unitOfWork.UserRepository.GetUserByIdAsync(userId);
            if (user == null) return false;

            var avatarUrl = await _storageService.UploadFileAsync(AvatarBucket, userId, avatarFile);
            user.Avatarurl = avatarUrl;

            await _unitOfWork.SaveChangesAsync();

            // Try auto-activate profile if all conditions met
            await TryAutoActivateProfileAsync(userId);

            return true;
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

            await TryAutoActivateProfileAsync(userId);

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

        // ─── Private helpers ─────────────────────────────────────────────────

        private static void ValidateImageFile(IFormFile file)
        {
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png" };
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

            if (!allowedExtensions.Contains(extension))
            {
                throw new ArgumentException("Chỉ chấp nhận ảnh JPG và PNG cho ảnh đại diện");
            }

            // 5MB limit
            if (file.Length > 5 * 1024 * 1024)
            {
                throw new ArgumentException("Ảnh đại diện phải nhỏ hơn 5MB");
            }
        }
    }
}
