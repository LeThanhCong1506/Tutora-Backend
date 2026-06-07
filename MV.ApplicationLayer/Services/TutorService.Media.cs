using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;

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

            var allowedExtensions = new[] { ".mp4", ".avi", ".mov", ".wmv", ".webm" };
            var fileExtension = Path.GetExtension(request.VideoFile.FileName).ToLowerInvariant();

            if (!allowedExtensions.Contains(fileExtension))
            {
                throw new ArgumentException("Chỉ chấp nhận file video (mp4, avi, mov, wmv, webm).");
            }

            // Phase 1: Liveness Check
            try
            {
                using var videoStream = request.VideoFile.OpenReadStream();
                var livenessResult = await _fptAiService.CheckVideoLivenessAsync(videoStream, request.VideoFile.FileName);

                if (livenessResult.Data != null)
                {
                    if (!livenessResult.Data.IsLive)
                    {
                        var attackType = livenessResult.Data.AttackType ?? DisplayValues.UnknownLower;
                        throw new ArgumentException($"Video không đạt yêu cầu xác thực người thật. Phát hiện: {attackType}. Vui lòng quay video mới với khuôn mặt rõ ràng.");
                    }

                    if (livenessResult.Data.LivenessScore < LIVENESS_THRESHOLD)
                    {
                        throw new ArgumentException($"Độ tin cậy video quá thấp ({livenessResult.Data.LivenessScore:F1}%). Vui lòng quay video trong điều kiện ánh sáng tốt hơn.");
                    }

                    if (!livenessResult.Data.FaceDetected)
                    {
                        throw new ArgumentException("Không phát hiện khuôn mặt trong video. Vui lòng đảm bảo khuôn mặt hiển thị rõ ràng.");
                    }

                    _logger.LogInformation("Liveness check passed for user {UserId}: Score={Score}, Quality={Quality}",
                        userId, livenessResult.Data.LivenessScore, livenessResult.Data.VideoQuality);
                }
            }
            catch (ArgumentException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Liveness check failed for user {UserId}, allowing upload anyway", userId);
            }

            var videoUrl = await _storageService.UploadFileAsync(VideoBucket, userId, request.VideoFile);

            profile.Videointrourl = videoUrl;
            profile.Updatedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

            await _unitOfWork.SaveChangesAsync();

            // Try auto-activate profile if all conditions met
            await TryAutoActivateProfileAsync(userId);

            return true;
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
