using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Helpers;
using MV.ApplicationLayer.Interfaces;

namespace MV.ApplicationLayer.Services
{
    public partial class TutorVerificationService : ITutorVerificationService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IFptAiService _fptAiService;
        private readonly IDistributedCache _cache;
        private readonly IAppDbContext _dbContext;
        private readonly IEncryptionService _encryption;
        private readonly ILogger<TutorVerificationService> _logger;

        private const string CacheKeyPrefix = "tutor_preview:";
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15);

        private const string FullProfileCacheKeyPrefix = "tutor_full_profile:";
        private static readonly TimeSpan FullProfileCacheDuration = TimeSpan.FromMinutes(20);
        private const string ProfileInfoCacheKeyPrefix = "tutor_profile_info:";
        private static readonly TimeSpan ProfileInfoCacheDuration = TimeSpan.FromMinutes(20);
        private const string ScheduleCacheKeyPrefix = "tutor_schedule:";
        private static readonly TimeSpan ScheduleCacheDuration = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan CacheOperationTimeout = TimeSpan.FromMilliseconds(200);

        public TutorVerificationService(
            IUnitOfWork unitOfWork,
            IFptAiService fptAiService,
            IDistributedCache cache,
            IAppDbContext dbContext,
            IEncryptionService encryption,
            ILogger<TutorVerificationService> logger)
        {
            _unitOfWork = unitOfWork;
            _fptAiService = fptAiService;
            _cache = cache;
            _dbContext = dbContext;
            _encryption = encryption;
            _logger = logger;
        }

        /// <summary>
        /// Tính toán Final Profile Status dựa trên tất cả các điều kiện
        /// Logic mới đơn giản:
        /// - Draft: Chưa đủ required fields hoặc chưa Identity Verified
        /// - Active: Identity Verified + Đủ required fields + Certificate valid (hoặc không cần certificate)
        /// - PendingApproval: User submit cho admin review (certificate invalid)
        /// - Rejected: Admin từ chối
        /// </summary>
        public async Task<string> CalculateFinalProfileStatusAsync(string userId)
        {
            try
            {
                var user = await _unitOfWork.UserRepository.GetUserByIdAsync(userId);
                var profile = await _unitOfWork.TutorRepository.GetTutorProfileByIdAsync(userId);
                var subjects = await _unitOfWork.TutorRepository.GetTutorSubjectsByTutorIdAsync(userId);
                var prices = await _unitOfWork.TutorRepository.GetTutorSubjectGradePricesAsync(userId);

                if (user == null || profile == null)
                {
                    _logger.LogWarning("User or profile not found for status calculation: {UserId}", userId);
                    return TutorProfileStatus.Draft;
                }

                // 1. Kiểm tra Identity Verified (Phase 1)
                bool identityVerified = user.Isidentityverified ?? false;

                // 2. Kiểm tra Required Fields đã điền đủ chưa
                bool hasRequiredFields = CheckRequiredFields(profile, user, subjects, prices);

                // 3. Quyết định Final Status
                string newStatus;

                if (!identityVerified || !hasRequiredFields)
                {
                    // Chưa đủ điều kiện cơ bản
                    newStatus = TutorProfileStatus.Draft;
                }
                else
                {
                    // Identity OK + Required fields OK → Active
                    newStatus = TutorProfileStatus.Active;
                }

                // 4. Update Profile Status nếu thay đổi
                if (!string.Equals(profile.Profilestatus, newStatus, StringComparison.OrdinalIgnoreCase))
                {
                    var oldStatus = profile.Profilestatus;
                    profile.Profilestatus = newStatus;
                    profile.Updatedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

                    if (string.Equals(newStatus, TutorProfileStatus.Active, StringComparison.OrdinalIgnoreCase))
                    {
                        profile.Ispublic = true; // Tự động public khi active
                    }

                    await _unitOfWork.SaveChangesAsync();

                    _logger.LogInformation(
                        "Profile status updated for user {UserId}: {OldStatus} -> {NewStatus}",
                        userId, oldStatus, newStatus);
                }

                return newStatus;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating final profile status for user {UserId}", userId);
                return TutorProfileStatus.Draft;
            }
        }

        /// <summary>
        /// Kiểm tra các trường bắt buộc đã được điền chưa
        /// Yêu cầu: Giá, Môn học, Thời gian dạy, Introduction, Avatar, Video
        /// </summary>
        private bool CheckRequiredFields(
            Tutorprofile profile,
            User user,
            List<Tutorsubject>? subjects,
            List<Tutorsubjectgradeprice>? prices)
        {
            // Basic Info
            bool hasHeadline = !string.IsNullOrWhiteSpace(profile.Headline);
            bool hasTeachingArea = !string.IsNullOrWhiteSpace(profile.Teachingareacity);
            bool hasSubjects = subjects != null && subjects.Count > 0;

            // Introduction
            bool hasBio = !string.IsNullOrWhiteSpace(profile.Bio);
            bool hasEducation = !string.IsNullOrWhiteSpace(profile.Education);

            // Pricing
            bool hasHourlyRate = prices != null && prices.Any(p => p.Isactive && p.Priceperhour > 0);

            // Media
            bool hasAvatar = !string.IsNullOrWhiteSpace(user.Avatarurl);
            bool hasVideo = !string.IsNullOrWhiteSpace(profile.Videointrourl);

            return hasHeadline && hasTeachingArea && hasSubjects &&
                   hasBio && hasEducation && hasHourlyRate && hasAvatar && hasVideo;
        }



        /// <summary>
        /// Get verification progress for a tutor - returns status of all sections
        /// </summary>
        public async Task<bool> UpdateTutorStatusToPendingAsync(string userId)
        {
            var profile = await _unitOfWork.TutorRepository.GetTutorProfileByIdAsync(userId);
            if (profile == null)
            {
                return false;
            }

            profile.Profilestatus = TutorProfileStatus.PendingApproval;
            profile.Updatedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

            await _unitOfWork.SaveChangesAsync();

            return true;
        }
    }
}
