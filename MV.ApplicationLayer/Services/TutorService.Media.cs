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

            // FPT.AI liveness chỉ nhận MP4, tối đa 10MB
            var fileExtension = Path.GetExtension(request.VideoFile.FileName).ToLowerInvariant();
            if (fileExtension != ".mp4")
            {
                throw new ArgumentException("Chỉ chấp nhận video định dạng MP4 để xác thực người thật.");
            }

            if (request.VideoFile.Length > 10 * 1024 * 1024)
            {
                throw new ArgumentException("Video phải nhỏ hơn 10MB.");
            }

            // ─── Liveness Check (FAIL-CLOSED: chỉ upload + lưu DB khi xác thực ĐẠT) ───
            FptAiLivenessResponse livenessResult;
            try
            {
                using var videoStream = request.VideoFile.OpenReadStream();
                livenessResult = await _fptAiService.CheckVideoLivenessAsync(videoStream, request.VideoFile.FileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Liveness check threw for user {UserId}", userId);
                throw new ArgumentException("Không thể xác thực video lúc này. Vui lòng thử lại sau.");
            }

            // Request không thành công hoặc không có dữ liệu liveness -> từ chối
            if (livenessResult == null || !livenessResult.IsRequestOk || livenessResult.Liveness == null)
            {
                var reason = MapLivenessError(livenessResult);
                _logger.LogWarning("Liveness rejected for user {UserId}: {Reason}", userId, reason);
                throw new ArgumentException(reason);
            }

            var data = livenessResult.Liveness;

            // Kết quả liveness của chính FPT không phải 200 -> map lỗi tương ứng
            if (data.Code != "200")
            {
                throw new ArgumentException(MapLivenessError(livenessResult));
            }

            if (data.IsDeepfake)
            {
                throw new ArgumentException("Video có dấu hiệu deepfake. Vui lòng quay video selfie thật.");
            }

            if (!data.IsLive)
            {
                throw new ArgumentException("Video không vượt qua kiểm tra người thật. Vui lòng quay lại video selfie rõ khuôn mặt, có cử động.");
            }

            _logger.LogInformation("Liveness passed for user {UserId}: SpoofProb={SpoofProb}", userId, data.SpoofProb);

            // Chỉ tới đây (đã verify đạt) mới upload Cloudinary + lưu DB
            var videoUrl = await _storageService.UploadFileAsync(VideoBucket, userId, request.VideoFile);

            profile.Videointrourl = videoUrl;
            profile.Updatedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

            await _unitOfWork.SaveChangesAsync();

            // Try auto-activate profile if all conditions met
            await TryAutoActivateProfileAsync(userId);

            return true;
        }

        /// <summary>Map mã lỗi liveness của FPT.AI sang thông báo tiếng Việt cho người dùng.</summary>
        private static string MapLivenessError(FptAiLivenessResponse? result)
        {
            var code = result?.Liveness?.Code ?? result?.Code;
            return code switch
            {
                "406" => "Chất lượng khuôn mặt chưa đạt (bị che, quá tối hoặc quá sáng). Vui lòng quay lại.",
                "408" => "Phát hiện nhiều khuôn mặt trong video. Vui lòng chỉ quay một mình bạn.",
                "409" => "Video không hợp lệ hoặc quá ngắn. Cần quay 5-6 giây, định dạng MP4.",
                "410" => "Không phát hiện khuôn mặt trong video. Vui lòng quay rõ khuôn mặt xuyên suốt.",
                "411" => "Khuôn mặt quá nhỏ trong khung hình. Vui lòng lại gần camera hơn.",
                "412" => "Khuôn mặt quá mờ. Vui lòng quay trong điều kiện ánh sáng tốt hơn.",
                "413" => "Video giống ảnh tĩnh. Vui lòng quay video selfie thật, có cử động.",
                "422" => "Khuôn mặt không nhìn thẳng. Vui lòng nhìn thẳng vào camera.",
                "423" => "Khuôn mặt ra khỏi khung hình khi quay. Vui lòng giữ mặt trong khung.",
                _ => "Video không vượt qua kiểm tra xác thực người thật. Vui lòng quay lại video selfie rõ khuôn mặt."
            };
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
