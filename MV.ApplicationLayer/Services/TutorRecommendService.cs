using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.Services
{
    /// <summary>
    /// Orchestrates: SQL hard-filter → AI ranking → full profile fetch → TutorRecommendResponse.
    /// </summary>
    public class TutorRecommendService : ITutorRecommendService
    {
        private const int CandidatePoolSize = 50;

        private readonly IUnitOfWork _unitOfWork;
        private readonly IAppDbContext _dbContext;
        private readonly ITutorAiClient _aiClient;
        private readonly ILogger<TutorRecommendService> _logger;

        public TutorRecommendService(
            IUnitOfWork unitOfWork,
            IAppDbContext dbContext,
            ITutorAiClient aiClient,
            ILogger<TutorRecommendService> logger)
        {
            _unitOfWork = unitOfWork;
            _dbContext = dbContext;
            _aiClient = aiClient;
            _logger = logger;
        }

        public async Task<TutorRecommendResponse> RecommendAsync(
            TutorRecommendRequest request,
            CancellationToken cancellationToken = default)
        {
            // Phase 1: SQL hard-filter → candidate pool
            var gradeName = request.GradeLevel;
            if (string.IsNullOrWhiteSpace(gradeName) && request.GradeLevelId.HasValue)
            {
                gradeName = await _dbContext.Gradelevels
                    .AsNoTracking()
                    .Where(g => g.Gradelevelid == request.GradeLevelId.Value && g.IsActive)
                    .Select(g => g.Gradename)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            var searchParams = BuildSearchParameters(request, gradeName);
            var searchResult = await _unitOfWork.TutorSearchRepository
                .SearchTutorsAsync(searchParams);

            var candidateIds = searchResult.Items
                .Select(t => t.TutorId)
                .Distinct()
                .ToList();

            if (candidateIds.Count == 0)
            {
                return new TutorRecommendResponse
                {
                    Tutors = new List<TutorRecommendItem>(),
                    Total = 0,
                    AiRanked = false
                };
            }

            // Phase 2: AI ranking
            var aiResults = await _aiClient.RankAsync(
                request.Query,
                candidateIds,
                request.TopK,
                cancellationToken);

            bool aiRanked;
            List<(string TutorId, float? Similarity)> rankedPairs;

            if (aiResults != null && aiResults.Count > 0)
            {
                aiRanked = true;
                rankedPairs = aiResults
                    .Select(r => (r.TutorId, (float?)r.Similarity))
                    .ToList();
            }
            else
            {
                // Graceful degrade: keep SQL order, no similarity scores
                aiRanked = false;
                rankedPairs = candidateIds
                    .Take(request.TopK)
                    .Select(id => (id, (float?)null))
                    .ToList();
            }

            // Phase 3: Fetch full profiles for ranked IDs
            var rankedIds = rankedPairs.Select(p => p.TutorId).ToList();
            var profileLookup = await FetchTutorProfilesAsync(rankedIds, cancellationToken);

            var items = rankedPairs
                .Where(p => profileLookup.ContainsKey(p.TutorId))
                .Select(p =>
                {
                    var item = profileLookup[p.TutorId];
                    item.AiSimilarity = p.Similarity;
                    return item;
                })
                .ToList();

            return new TutorRecommendResponse
            {
                Tutors = items,
                Total = items.Count,
                AiRanked = aiRanked
            };
        }

        // Private Helpers

        /// <summary>
        /// Maps TutorRecommendRequest to TutorSearchParameters for the SQL candidate filter.
        /// Always fetches up to CandidatePoolSize records (page 1, large page).
        /// </summary>
        private static TutorSearchParameters BuildSearchParameters(TutorRecommendRequest request, string? gradeName)
        {
            return new TutorSearchParameters
            {
                PageNumber = 1,
                PageSize = CandidatePoolSize,
                SubjectIds = request.SubjectId.HasValue
                    ? new List<int> { request.SubjectId.Value }
                    : null,
                GradeLevel = gradeName,
                TeachingMode = request.TeachingMode,
                TeachingAreaCity = request.City,
                TeachingAreaDistrict = request.District,
                MinHourlyRate = request.MinRate,
                MaxHourlyRate = request.MaxRate,
                SortBy = TutorSearchSortBy.Default   
            };
        }

        /// <summary>
        /// Fetch users + tutor profiles + min price for the given tutor IDs.
        /// </summary>
        private async Task<Dictionary<string, TutorRecommendItem>> FetchTutorProfilesAsync(
            IReadOnlyList<string> tutorIds,
            CancellationToken cancellationToken)
        {
            if (tutorIds.Count == 0)
                return new Dictionary<string, TutorRecommendItem>();

            var rows = await _dbContext.Users
                .AsNoTracking()
                .Where(u => tutorIds.Contains(u.Userid) &&
                            u.Tutorprofile != null &&
                            u.Status == 1 &&
                            u.Tutorprofile.Profilestatus != null &&
                            u.Tutorprofile.Profilestatus.ToLower() == TutorProfileStatus.Active &&
                            u.Tutorprofile.Ispublic == true)
                .Select(u => new
                {
                    u.Userid,
                    FullName = u.Fullname ?? "",
                    u.Avatarurl,
                    u.Tutorprofile!.Headline,
                    u.Tutorprofile.Teachingareacity,
                    u.Tutorprofile.Teachingareadistrict,
                    AverageRating = u.Tutorprofile.Averagerating ?? 0,
                    TotalReviews = u.Tutorprofile.Totalreviews ?? 0,
                    CompletedHours = u.Tutorprofile.Completedhours ?? 0,
                    // Bỏ qua bảng giá gắn môn/khối đã bị soft-delete (Subject/Gradelevel.IsActive).
                    MinPrice = u.Tutorprofile.Tutorsubjectgradeprices
                        .Where(p => p.Isactive && p.Subject.IsActive && p.Gradelevel.IsActive)
                        .Min(p => (decimal?)p.Priceperhour),
                    Subjects = u.Tutorprofile.Tutorsubjectgradeprices
                        .Where(p => p.Subject != null && p.Subject.IsActive && p.Subject.Subjectname != null)
                        .Select(p => p.Subject!.Subjectname!)
                        .Distinct()
                        .ToList()
                })
                .ToListAsync(cancellationToken);

            return rows.ToDictionary(
                r => r.Userid,
                r => new TutorRecommendItem
                {
                    TutorId = r.Userid,
                    FullName = r.FullName,
                    AvatarUrl = r.Avatarurl,
                    Headline = r.Headline,
                    TeachingMode = TeachingMode.Online,  
                    TeachingAreaCity = r.Teachingareacity,
                    TeachingAreaDistrict = r.Teachingareadistrict,
                    AverageRating = r.AverageRating,
                    TotalReviews = r.TotalReviews,
                    CompletedHours = r.CompletedHours,
                    PricePerHour = r.MinPrice,
                    Subjects = r.Subjects,
                    AiSimilarity = null,    
                    ProfileUrl = $"/tutor-detail/{r.Userid}"
                });
        }
    }
}
