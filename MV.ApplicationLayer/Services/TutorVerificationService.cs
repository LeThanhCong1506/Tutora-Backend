using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Helpers;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.RepositoryInterfaces;

namespace MV.ApplicationLayer.Services
{
    public partial class TutorVerificationService : ITutorVerificationService
    {
        private readonly ITutorRepository _tutorRepository;
        private readonly IUserRepository _userRepository;
        private readonly IFptAiService _fptAiService;
        private readonly IDistributedCache _cache;
        private readonly IAppDbContext _dbContext;
        private readonly ILogger<TutorVerificationService> _logger;
        private readonly IEncryptionService _encryption;
        private readonly ITutorProfileUpdateStagingService _updateStaging;

        private const string CacheKeyPrefix = "tutor_preview:";
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15);

        private const string ProfileInfoCacheKeyPrefix = "tutor_profile_info:";
        private static readonly TimeSpan ProfileInfoCacheDuration = TimeSpan.FromMinutes(20);
        private const string ScheduleCacheKeyPrefix = "tutor_schedule:";
        private static readonly TimeSpan ScheduleCacheDuration = TimeSpan.FromMinutes(10);

        public TutorVerificationService(
            ITutorRepository tutorRepository,
            IUserRepository userRepository,
            IFptAiService fptAiService,
            IDistributedCache cache,
            IAppDbContext dbContext,
            ILogger<TutorVerificationService> logger,
            IEncryptionService encryption,
            ITutorProfileUpdateStagingService updateStaging)
        {
            _tutorRepository = tutorRepository;
            _userRepository = userRepository;
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
            var profile = await _tutorRepository.GetTutorProfileByIdAsync(userId);
            if (profile == null)
            {
                return false;
            }

            profile.Profilestatus = TutorProfileStatus.PendingApproval;
            profile.Updatedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

            await _dbContext.SaveChangesAsync();

            return true;
        }
    }
}
