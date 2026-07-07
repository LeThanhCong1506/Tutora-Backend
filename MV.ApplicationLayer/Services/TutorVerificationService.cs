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
        private readonly ILogger<TutorVerificationService> _logger;
        private readonly IEncryptionService _encryption;
        private readonly ITutorProfileUpdateStagingService _updateStaging;

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
            ILogger<TutorVerificationService> logger,
            IEncryptionService encryption,
            ITutorProfileUpdateStagingService updateStaging)
        {
            _unitOfWork = unitOfWork;
            _fptAiService = fptAiService;
            _cache = cache;
            _dbContext = dbContext;
            _logger = logger;
            _encryption = encryption;
            _updateStaging = updateStaging;
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
