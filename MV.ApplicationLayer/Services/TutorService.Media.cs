using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Helpers;

namespace MV.ApplicationLayer.Services
{
    public partial class TutorService
    {
        // ─── Media Methods (avatar + video) ─────────────────────────────────

        public async Task<string?> UpdateTutorAvatarAsync(string userId, IFormFile avatarFile)
        {
            if (avatarFile == null || avatarFile.Length == 0)
                throw new ArgumentException("Vui lòng chọn ảnh đại diện");

            ValidateImageFile(avatarFile);

            var user = await _userRepository.GetUserByIdAsync(userId);
            if (user == null) return null;

            var avatarUrl = await _storageService.UploadFileAsync(AvatarBucket, userId, avatarFile);
            user.Avatarurl = avatarUrl;

            await _context.SaveChangesAsync();
            return avatarUrl;
        }

        public async Task<bool> UpdateTutorVideoAsync(string userId, UpdateTutorVideoRequest request)
        {
            var profile = await _tutorRepository.GetTutorProfileByIdAsync(userId);
            if (profile == null) return false;

            if (!IsValidYoutubeUrl(request.VideoUrl))
                throw new ArgumentException("Vui lòng nhập link video YouTube hợp lệ.");

            var trimmedUrl = request.VideoUrl.Trim();

            if (RequiresApprovalForEdits(profile))
            {
                await _updateStaging.UpsertPendingUpdateAsync(userId, pending =>
                {
                    pending.VideoIntroUrl = trimmedUrl;
                });
                await NotifyAdminsOfProfileUpdateAsync(userId, "Video giới thiệu");
                return true;
            }

            profile.Videointrourl = trimmedUrl;
            profile.Updatedat = TimeZoneHelper.UtcNow;

            await _context.SaveChangesAsync();
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
            var user = await _userRepository.GetUserByIdAsync(userId)
                ?? throw new ArgumentException("Không tìm thấy người dùng.");

            // Dùng luồng eKYC (FPT.AI OCR + chống trùng). Tutor cho phép OCR thất bại
            // (Admin xác minh thủ công), không gate độ tuổi.
            //
            // AutoFillProfile = false: dữ liệu OCR KHÔNG tự ghi đè hồ sơ nữa. Gia sư phải xem
            // màn hình đối chiếu rồi bấm xác nhận (POST .../cccd/confirm) thì mới ghi — tránh
            // việc tên/ngày sinh trên tài khoản đổi sau lưng khi OCR đọc nhầm.
            var result = await _ekyc.VerifyAndApplyAsync(user, request, new EkycVerificationOptions
            {
                RequireOcr = false,
                MinAgeRequired = null,
                AutoFillProfile = false
            });

            await _userRepository.UpdateUserAsync(user);
            await _context.SaveChangesAsync();

            _logger.LogInformation("CCCD uploaded for user {UserId}, OCR={OcrSuccess}, Verified={Verified}",
                userId, result.Ocr != null, result.Verified);

            if (result.Verified)
                await AutoSubmitIfCompleteAsync(userId);

            return result.Response;
        }

        public async Task<CccdProfileConfirmResponse> ConfirmCccdProfileAsync(string userId)
        {
            var user = await _userRepository.GetUserByIdAsync(userId)
                ?? throw new ArgumentException("Không tìm thấy người dùng.");

            var response = _ekyc.ApplyStoredProfileData(user);

            await _userRepository.UpdateUserAsync(user);
            await _context.SaveChangesAsync();

            _logger.LogInformation("CCCD profile confirmed for user {UserId}, {ChangeCount} field(s) applied",
                userId, response.AppliedChanges.Count);

            return response;
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

    }
}
