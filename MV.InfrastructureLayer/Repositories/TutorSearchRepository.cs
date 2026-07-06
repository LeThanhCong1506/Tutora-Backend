using Microsoft.EntityFrameworkCore;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer.DBContext;
using MV.ApplicationLayer.RepositoryInterfaces;
using System.Text.Json;
using MV.DomainLayer.Constants;

namespace MV.InfrastructureLayer.Repositories
{
    /// <summary>
    /// Repository implementation for tutor search operations
    /// Uses PostgreSQL Full-Text Search with unaccent and pg_trgm extensions
    /// Matches Figma UI design for tutor search page
    /// </summary>
    public class TutorSearchRepository : ITutorSearchRepository
    {
        private readonly AgoraDbContext _context;

        private static readonly string[] PaidBookingStatuses =
        [
            BookingStatus.DepositPaid,
            BookingStatus.PendingRemainingPayment,
            BookingStatus.Paid,
            BookingStatus.Ongoing,
            BookingStatus.Completed
        ];

        // Category to Subject mapping (for category tabs)
        private static readonly Dictionary<string, List<string>> CategorySubjectMapping = new()
        {
            { TutorSearchCategory.Science,  new List<string> { "toán", "vật lý", "hóa học", "sinh học", "math", "physics", "chemistry", "biology", "calculus", "statistics" } },
            { TutorSearchCategory.Language, new List<string> { "tiếng anh", "ielts", "toeic", "toefl", "sat", "english", "tiếng pháp", "tiếng nhật", "tiếng hàn", "tiếng trung" } },
            { TutorSearchCategory.Art,      new List<string> { "âm nhạc", "hội họa", "mỹ thuật", "music", "art", "design", "piano", "guitar", "vẽ" } },
            { TutorSearchCategory.ItTech,   new List<string> { "lập trình", "it", "programming", "web development", "python", "java", "javascript", "data science", "ai", "ux design" } },
            // Individual subject categories (used by frontend tabs)
            { TutorSearchCategory.Math,      new List<string> { "toán", "math", "calculus", "statistics", "đại số", "hình học", "giải tích" } },
            { TutorSearchCategory.Physics,   new List<string> { "vật lý", "physics", "cơ học", "điện", "quang học" } },
            { TutorSearchCategory.Chemistry, new List<string> { "hóa học", "chemistry", "hóa hữu cơ", "hóa vô cơ" } },
            { TutorSearchCategory.English,   new List<string> { "tiếng anh", "english", "ielts", "toeic", "toefl", "sat", "giao tiếp" } },
        };

        // Subscription type labels
        private static readonly Dictionary<string, string> SubscriptionTypeLabels = new()
        {
            { SubscriptionType.Free,      "FREE TUTOR" },
            { SubscriptionType.Guided,    "GUIDED TUTOR" },
            { SubscriptionType.Intensive, "INTENSIVE TUTOR" },
            { SubscriptionType.Elite,     "ELITE TUTOR" }
        };

        public TutorSearchRepository(AgoraDbContext context)
        {
            _context = context;
        }

        public async Task<TutorSearchPagedResponse> SearchTutorsAsync(TutorSearchParameters parameters)
        {
            // Base query: Get all approved tutors with their profiles
            // NOTE: Only Include what's needed for search results list
            var query = _context.Users
                .AsNoTracking()
                .Include(u => u.Tutorprofile)
                    .ThenInclude(tp => tp!.Tutorsubjectgradeprices)
                        .ThenInclude(ts => ts.Subject)
                .Include(u => u.Tutorprofile)
                    .ThenInclude(tp => tp!.Tutorsubjectgradeprices)
                        .ThenInclude(ts => ts.Gradelevel)
                // Removed Tutorcertificates Include - not needed for list view, saves significant query time
                // Removed Tutorcertificates Include - not needed for list view, saves significant query time
                .Where(u => u.Tutorprofile != null &&
                            u.Status == 1 &&
                            u.Tutorprofile.Profilestatus.ToLower() == TutorProfileStatus.Active &&
                            u.Tutorprofile.Ispublic == true &&
                            u.Tutorprofile.Isacceptingbookings == true)
                .AsQueryable();

            // ==================== APPLY FILTERS ====================

            // 1. Search Term (Fuzzy search with unaccent) - Only use ILIKE for search
            if (!string.IsNullOrWhiteSpace(parameters.SearchTerm))
            {
                var searchTerm = parameters.SearchTerm.Trim().ToLower();
                query = query.Where(u =>
                    EF.Functions.ILike(u.Fullname ?? "", $"%{searchTerm}%") ||
                    EF.Functions.ILike(u.Tutorprofile!.Headline ?? "", $"%{searchTerm}%") ||
                    EF.Functions.ILike(u.Tutorprofile!.Education ?? "", $"%{searchTerm}%") ||
                    u.Tutorprofile.Tutorsubjectgradeprices.Any(ts =>
                        EF.Functions.ILike(ts.Subject!.Subjectname ?? "", $"%{searchTerm}%"))
                );
            }

            // 2. Filter by Category (Maps to tabs in Figma) - Use Contains for subject names
            if (!string.IsNullOrWhiteSpace(parameters.Category) && parameters.Category.ToLower() != TutorSearchCategory.AllValue)
            {
                var category = parameters.Category.ToLower();
                if (CategorySubjectMapping.TryGetValue(category, out var subjectKeywords))
                {
                    query = query.Where(u =>
                        u.Tutorprofile!.Tutorsubjectgradeprices.Any(ts =>
                            ts.Subject != null &&
                            ts.Subject.Subjectname != null &&
                            subjectKeywords.Any(keyword =>
                                ts.Subject.Subjectname.ToLower().Contains(keyword))));
                }
            }

            // 3. Filter by Subject IDs - EXACT MATCH (fast with index)
            if (parameters.SubjectIds != null && parameters.SubjectIds.Any())
            {
                query = query.Where(u =>
                    u.Tutorprofile!.Tutorsubjectgradeprices.Any(ts =>
                        parameters.SubjectIds.Contains(ts.Subjectid)));
            }

            // 4. Filter by Subscription Types - EXACT MATCH (fast)
            if (parameters.SubscriptionTypes != null && parameters.SubscriptionTypes.Any())
            {
                var lowerTypes = parameters.SubscriptionTypes.Select(t => t.ToLower()).ToList();
                query = query.Where(u =>
                    u.Tutorprofile!.Subscriptiontype != null &&
                    lowerTypes.Contains(u.Tutorprofile.Subscriptiontype.ToLower()));
            }

            // 5. Filter by Budget Range - EXACT comparison (fast with index)
            if (!string.IsNullOrWhiteSpace(parameters.BudgetRange) && parameters.BudgetRange != TutorSearchBudgetRange.AllValue)
            {
                var (minPrice, maxPrice) = GetPriceRangeFromBudget(parameters.BudgetRange);
                query = ApplyPriceRangeFilter(query, minPrice, maxPrice);
            }
            else
            {
                // Custom price range
                query = ApplyPriceRangeFilter(query, parameters.MinHourlyRate, parameters.MaxHourlyRate);
            }

            // 6. Filter by Minimum Rating - EXACT comparison (fast)
            if (parameters.MinRating.HasValue)
            {
                query = query.Where(u => u.Tutorprofile!.Averagerating >= parameters.MinRating.Value);
            }

            // 7. Filter by Minimum Years of Experience
            if (parameters.MinYearsExperience.HasValue)
            {
                var minHours = parameters.MinYearsExperience.Value * 500;
                query = query.Where(u => u.Tutorprofile!.Completedhours >= minHours);
            }

            // 8. Filter by Teaching Area City - EXACT MATCH (fast with index)
            if (!string.IsNullOrWhiteSpace(parameters.TeachingAreaCity))
            {
                var city = parameters.TeachingAreaCity.Trim();
                query = query.Where(u => u.Tutorprofile!.Teachingareacity == city);
            }

            // 9. Filter by Teaching Area District - EXACT MATCH (fast)
            if (!string.IsNullOrWhiteSpace(parameters.TeachingAreaDistrict))
            {
                var district = parameters.TeachingAreaDistrict.Trim();
                query = query.Where(u => u.Tutorprofile!.Teachingareadistrict == district);
            }

            // 14. Filter by Grade Level - Search in JSONB gradelevels column
            if (!string.IsNullOrWhiteSpace(parameters.GradeLevel))
            {
                var gradeLevelKey = parameters.GradeLevel.Trim();
                var pattern = $"%{gradeLevelKey}%";

                var matchingTutorIds = _context.Tutorsubjectgradeprices
                    .Where(p => p.Gradelevel.Gradename != null && EF.Functions.ILike(p.Gradelevel.Gradename, pattern))
                    .Select(ts => ts.Tutorid)
                    .ToList();
                
                if (matchingTutorIds.Any())
                {
                    query = query.Where(u => matchingTutorIds.Contains(u.Userid));
                }
                else
                {
                    // No matching tutors found, return empty query to prevent fetching all
                    query = query.Where(u => false);
                }
            }

            // ==================== APPLY SORTING ====================
            query = ApplySorting(query, parameters.SortBy);

            // ==================== COUNT TOTAL ====================
            var totalCount = await query.CountAsync();

            // ==================== PAGINATION ====================
            var items = await query
                .Skip((parameters.PageNumber - 1) * parameters.PageSize)
                .Take(parameters.PageSize)
                .ToListAsync();

            // ==================== LESSON & STUDENT COUNTS (cho trang hiện tại) ====================
            var tutorIds = items.Select(u => u.Userid).ToList();

            var classSessionCounts = await _context.ClassSessions
                .Where(l => l.Tutorid != null && tutorIds.Contains(l.Tutorid)
                    && l.Status != ClassSessionStatus.Cancelled
                    && l.Status != ClassSessionStatus.CancelledNoshow
                    && l.Booking != null && PaidBookingStatuses.Contains(l.Booking.Status!))
                .GroupBy(l => l.Tutorid!)
                .Select(g => new { TutorId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.TutorId, x => x.Count);

            var studentCounts = await _context.Bookings
                .Where(b => b.Tutorid != null && tutorIds.Contains(b.Tutorid)
                    && PaidBookingStatuses.Contains(b.Status!))
                .Select(b => new { b.Tutorid, b.Studentid })
                .Distinct()
                .GroupBy(b => b.Tutorid!)
                .Select(g => new { TutorId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.TutorId, x => x.Count);

            // ==================== MAP TO RESPONSE ====================
            var results = items.Select(u =>
            {
                var dto = MapToSearchResult(u);
                dto.TotalClassSessions = classSessionCounts.TryGetValue(u.Userid, out var classSessionCount) ? classSessionCount : 0;
                dto.TotalStudents = studentCounts.TryGetValue(u.Userid, out var studentCount) ? studentCount : 0;
                return dto;
            }).ToList();
            // Grade Level filter is now applied at database level (before pagination)

            return new TutorSearchPagedResponse
            {
                Items = results,
                CurrentPage = parameters.PageNumber,
                PageSize = parameters.PageSize,
                TotalCount = totalCount,
                TotalPages = (int)Math.Ceiling(totalCount / (double)parameters.PageSize),
                ResultsText = $"{totalCount} KẾT QUẢ TÌM THẤY"
            };
        }

        public async Task<TutorSearchFilterMetadata> GetFilterMetadataAsync()
        {
            // Get real data from database
            var subjects = await GetAvailableSubjectsAsync();
            var cities = await GetAvailableCitiesAsync();
            var categories = await GetAvailableCategoriesAsync();
            var gradeLevels = await GetAvailableGradeLevelsAsync();

            // Get price and rating ranges from approved tutors
            var approvedTutors = _context.Tutorprofiles
                .AsNoTracking()
                .Include(tp => tp.Tutorsubjectgradeprices)
                .Where(tp => tp.Profilestatus.ToLower() == TutorProfileStatus.Active && tp.Ispublic == true && tp.Isacceptingbookings == true);

            decimal minPrice = 0, maxPrice = 0;
            double minRating = 0, maxRating = 5;

            if (await approvedTutors.AnyAsync())
            {
                var activePrices = _context.Tutorsubjectgradeprices
                    .AsNoTracking()
                    .Where(p => p.Isactive &&
                                p.Tutor.Profilestatus.ToLower() == TutorProfileStatus.Active &&
                                p.Tutor.Ispublic == true &&
                                p.Tutor.Isacceptingbookings == true);

                if (await activePrices.AnyAsync())
                {
                    minPrice = await activePrices.MinAsync(p => p.Priceperhour);
                    maxPrice = await activePrices.MaxAsync(p => p.Priceperhour);
                }

                minRating = await approvedTutors.MinAsync(tp => tp.Averagerating) ?? 0;
                maxRating = await approvedTutors.MaxAsync(tp => tp.Averagerating) ?? 5;
            }

            // Get budget ranges with actual tutor counts
            var budgetRanges = await GetBudgetRangesWithCountsAsync();

            // Get teaching modes with actual tutor counts
            var teachingModes = await GetTeachingModesWithCountsAsync();

            // Get sort options (static)
            var sortOptions = GetSortOptions();

            return new TutorSearchFilterMetadata
            {
                AvailableCategories = categories,
                AvailableGradeLevels = gradeLevels,
                AvailableBudgetRanges = budgetRanges,
                AvailableTeachingModes = teachingModes,
                AvailableSortOptions = sortOptions,
                AvailableSubjects = subjects,
                AvailableCities = cities,
                MinPriceInResults = minPrice,
                MaxPriceInResults = maxPrice,
                MinRatingInResults = minRating,
                MaxRatingInResults = maxRating
            };
        }

        /// <summary>
        /// Get all subjects from database that have approved tutors
        /// Returns actual subject list for dropdown
        /// </summary>
        public async Task<List<FilterOption>> GetAvailableSubjectsAsync()
        {
            var subjects = await _context.Subjects
                .AsNoTracking()
                .Where(s => s.Tutorsubjectgradeprices.Any(ts =>
                    ts.Tutor != null &&
                    ts.Tutor.Profilestatus == TutorProfileStatus.Active &&
                    ts.Tutor.Ispublic == true &&
                    ts.Tutor.Isacceptingbookings == true))
                .Select(s => new
                {
                    s.Subjectid,
                    s.Subjectname,
                    TutorCount = s.Tutorsubjectgradeprices.Count(ts =>
                        ts.Tutor != null &&
                        ts.Tutor.Profilestatus == TutorProfileStatus.Active &&
                        ts.Tutor.Ispublic == true &&
                        ts.Tutor.Isacceptingbookings == true)
                })
                .OrderByDescending(s => s.TutorCount)
                .ThenBy(s => s.Subjectname)
                .ToListAsync();

            return subjects.Select(s => new FilterOption
            {
                Value = s.Subjectid.ToString(),
                Label = s.Subjectname ?? DisplayValues.Unknown,
                Count = s.TutorCount
            }).ToList();
        }

        /// <summary>
        /// Get all DISTINCT cities from tutorprofiles where tutors are approved
        /// Returns actual city list for dropdown - EXACT values from database
        /// </summary>
        public async Task<List<FilterOption>> GetAvailableCitiesAsync()
        {
            var cities = await _context.Tutorprofiles
                .AsNoTracking()
                .Where(tp =>
                    tp.Profilestatus.ToLower() == TutorProfileStatus.Active &&
                    tp.Ispublic == true &&
                    tp.Isacceptingbookings == true &&
                    !string.IsNullOrEmpty(tp.Teachingareacity))
                .GroupBy(tp => tp.Teachingareacity)
                .Select(g => new
                {
                    City = g.Key,
                    Count = g.Count()
                })
                .OrderByDescending(c => c.Count)
                .ThenBy(c => c.City)
                .ToListAsync();

            return cities.Select(c => new FilterOption
            {
                Value = c.City!, // Exact value to use in filter
                Label = c.City!, // Display label
                Count = c.Count
            }).ToList();
        }

        /// <summary>
        /// Get categories with actual tutor counts
        /// </summary>
        public async Task<List<FilterOption>> GetAvailableCategoriesAsync()
        {
            var categories = new List<FilterOption>();

            // Get total count for "all" category
            var totalCount = await _context.Tutorprofiles
                .AsNoTracking()
                .CountAsync(tp => tp.Profilestatus.ToLower() == TutorProfileStatus.Active && tp.Ispublic == true && tp.Isacceptingbookings == true);

            categories.Add(new FilterOption { Value = TutorSearchCategory.AllValue, Label = "TẤT CẢ", Count = totalCount });

            // Calculate count for each category
            foreach (var (categoryKey, categoryLabel) in new[] {
                (TutorSearchCategory.Science,  "KHOA HỌC"),
                (TutorSearchCategory.Language, "NGÔN NGỮ"),
                (TutorSearchCategory.Art,      "NGHỆ THUẬT"),
                (TutorSearchCategory.ItTech,   "IT & TECH")
            })
            {
                if (CategorySubjectMapping.TryGetValue(categoryKey, out var keywords))
                {
                    var count = await _context.Users
                        .AsNoTracking()
                        .CountAsync(u =>
                            u.Tutorprofile != null &&
                            u.Tutorprofile.Profilestatus.ToLower() == TutorProfileStatus.Active &&
                            u.Tutorprofile.Ispublic == true &&
                            u.Tutorprofile.Isacceptingbookings == true &&
                            u.Tutorprofile.Tutorsubjectgradeprices.Any(ts =>
                                ts.Subject != null &&
                                ts.Subject.Subjectname != null &&
                                keywords.Any(k => ts.Subject.Subjectname.ToLower().Contains(k))));

                    categories.Add(new FilterOption { Value = categoryKey, Label = categoryLabel, Count = count });
                }
            }

            return categories;
        }

        /// <summary>
        /// Get grade levels from the normalized gradelevels table.
        /// </summary>
        public async Task<List<FilterOption>> GetAvailableGradeLevelsAsync()
        {
            var gradeLevels = await _context.Gradelevels
                .AsNoTracking()
                .Select(g => new
                {
                    g.Gradelevelid,
                    g.Gradename,
                    g.Levelorder,
                    TutorCount = g.Tutorsubjectgradeprices
                        .Where(p => p.Isactive &&
                                    p.Tutor.Profilestatus == TutorProfileStatus.Active &&
                                    p.Tutor.Ispublic == true &&
                                    p.Tutor.Isacceptingbookings == true)
                        .Select(p => p.Tutorid)
                        .Distinct()
                        .Count()
                })
                .OrderBy(g => g.Levelorder)
                .ThenBy(g => g.Gradename)
                .ToListAsync();

            return gradeLevels.Select(g => new FilterOption
            {
                Value = g.Gradelevelid.ToString(),
                Label = g.Gradename ?? DisplayValues.Unknown,
                Count = g.TutorCount
            }).ToList();
        }

        #region Private Helper Methods

        /// <summary>
        /// Get budget ranges with actual tutor counts - OPTIMIZED: Single query instead of 6
        /// </summary>
        private async Task<List<FilterOption>> GetBudgetRangesWithCountsAsync()
        {
            // Single query to get all counts using conditional aggregation
            var counts = await _context.Tutorprofiles
                .AsNoTracking()
                .Where(tp => tp.Profilestatus.ToLower() == TutorProfileStatus.Active && tp.Ispublic == true && tp.Isacceptingbookings == true)
                .GroupBy(tp => 1) // Group all into single group
                .Select(g => new
                {
                    Total = g.Count(),
                    Under50 = g.Count(tp => tp.Tutorsubjectgradeprices.Any(p => p.Isactive && p.Priceperhour < 50000)),
                    Range50_100 = g.Count(tp => tp.Tutorsubjectgradeprices.Any(p => p.Isactive && p.Priceperhour >= 50000 && p.Priceperhour < 100000)),
                    Range100_200 = g.Count(tp => tp.Tutorsubjectgradeprices.Any(p => p.Isactive && p.Priceperhour >= 100000 && p.Priceperhour < 200000)),
                    Range200_500 = g.Count(tp => tp.Tutorsubjectgradeprices.Any(p => p.Isactive && p.Priceperhour >= 200000 && p.Priceperhour < 500000)),
                    Over500 = g.Count(tp => tp.Tutorsubjectgradeprices.Any(p => p.Isactive && p.Priceperhour >= 500000))
                })
                .FirstOrDefaultAsync();

            if (counts == null)
            {
                return new List<FilterOption>
                {
                    new FilterOption { Value = TutorSearchBudgetRange.AllValue,    Label = "Mọi giá",       Count = 0 },
                    new FilterOption { Value = TutorSearchBudgetRange.Under50,     Label = "Dưới $50",      Count = 0 },
                    new FilterOption { Value = TutorSearchBudgetRange.Range50_100, Label = "$50 - $100",    Count = 0 },
                    new FilterOption { Value = TutorSearchBudgetRange.Range100_200,Label = "$100 - $200",   Count = 0 },
                    new FilterOption { Value = TutorSearchBudgetRange.Range200_500,Label = "$200 - $500",   Count = 0 },
                    new FilterOption { Value = TutorSearchBudgetRange.Over500,     Label = "Trên $500",     Count = 0 }
                };
            }

            return new List<FilterOption>
            {
                new FilterOption { Value = TutorSearchBudgetRange.AllValue,    Label = "Mọi giá",       Count = counts.Total },
                new FilterOption { Value = TutorSearchBudgetRange.Under50,     Label = "Dưới $50",      Count = counts.Under50 },
                new FilterOption { Value = TutorSearchBudgetRange.Range50_100, Label = "$50 - $100",    Count = counts.Range50_100 },
                new FilterOption { Value = TutorSearchBudgetRange.Range100_200,Label = "$100 - $200",   Count = counts.Range100_200 },
                new FilterOption { Value = TutorSearchBudgetRange.Range200_500,Label = "$200 - $500",   Count = counts.Range200_500 },
                new FilterOption { Value = TutorSearchBudgetRange.Over500,     Label = "Trên $500",     Count = counts.Over500 }
            };
        }

        /// <summary>
        /// Get teaching modes with actual tutor counts
        /// </summary>
                private Task<List<FilterOption>> GetTeachingModesWithCountsAsync()
        {
            return Task.FromResult(new List<FilterOption>
            {
                new FilterOption { Value = TeachingMode.Online, Label = "Online", Count = 0 }
            });
        }

        /// <summary>
        /// Get sort options (static)
        /// </summary>
        private static List<FilterOption> GetSortOptions()
        {
            return new List<FilterOption>
            {
                new FilterOption { Value = TutorSearchSortBy.RatingDesc,     Label = "Đánh giá cao nhất",   Count = 0 },
                new FilterOption { Value = TutorSearchSortBy.PriceAsc,       Label = "Giá thấp nhất",       Count = 0 },
                new FilterOption { Value = TutorSearchSortBy.PriceDesc,      Label = "Giá cao nhất",        Count = 0 },
                new FilterOption { Value = TutorSearchSortBy.ExperienceDesc, Label = "Thâm niên cao nhất",  Count = 0 },
                new FilterOption { Value = TutorSearchSortBy.ReviewsDesc,    Label = "Đánh giá nhiều nhất", Count = 0 },
                new FilterOption { Value = TutorSearchSortBy.Newest,         Label = "Mới nhất",            Count = 0 },
                new FilterOption { Value = TutorSearchSortBy.Popularity,     Label = "Phổ biến nhất",       Count = 0 }
            };
        }

        private static (decimal? min, decimal? max) GetPriceRangeFromBudget(string budgetRange)
        {
            return budgetRange.ToLower() switch
            {
                TutorSearchBudgetRange.Under50      => (null, 50000m),
                TutorSearchBudgetRange.Range50_100  => (50000m, 100000m),
                TutorSearchBudgetRange.Range100_200 => (100000m, 200000m),
                TutorSearchBudgetRange.Range200_500 => (200000m, 500000m),
                TutorSearchBudgetRange.Over500      => (500000m, null),
                _                                   => (null, null)
            };
        }

        private static IQueryable<User> ApplyPriceRangeFilter(IQueryable<User> query, decimal? minPrice, decimal? maxPrice)
        {
            if (!minPrice.HasValue && !maxPrice.HasValue) return query;

            return query.Where(u => u.Tutorprofile!.Tutorsubjectgradeprices.Any(p =>
                p.Isactive &&
                (!minPrice.HasValue || p.Priceperhour >= minPrice.Value) &&
                (!maxPrice.HasValue || p.Priceperhour <= maxPrice.Value)));
        }

        private static IQueryable<User> ApplySorting(IQueryable<User> query, string? sortBy)
        {
            return sortBy?.ToLower() switch
            {
                TutorSearchSortBy.RatingAsc      => query.OrderBy(u => u.Tutorprofile!.Averagerating ?? 0),
                TutorSearchSortBy.PriceAsc       => query.OrderBy(u => u.Tutorprofile!.Tutorsubjectgradeprices.Where(p => p.Isactive).Min(p => (decimal?)p.Priceperhour) ?? 0),
                TutorSearchSortBy.PriceDesc      => query.OrderByDescending(u => u.Tutorprofile!.Tutorsubjectgradeprices.Where(p => p.Isactive).Min(p => (decimal?)p.Priceperhour) ?? 0),
                TutorSearchSortBy.ExperienceDesc => query.OrderByDescending(u => u.Tutorprofile!.Completedhours ?? 0),
                TutorSearchSortBy.ReviewsDesc    => query.OrderByDescending(u => u.Tutorprofile!.Totalreviews ?? 0),
                TutorSearchSortBy.Newest         => query.OrderByDescending(u => u.Tutorprofile!.Createdat),
                TutorSearchSortBy.Popularity     => query.OrderByDescending(u => u.Tutorprofile!.Bookings.Count),
                _                                => query.OrderByDescending(u => u.Tutorprofile!.Averagerating ?? 0)
                                                        .ThenByDescending(u => u.Tutorprofile!.Totalreviews ?? 0)
            };
        }

        private static TutorSearchResultResponse MapToSearchResult(User user)
        {
            var profile = user.Tutorprofile!;

            // Calculate years of experience from completed hours (approx 500 hours = 1 year)
            int? yearsExperience = profile.Completedhours.HasValue
                ? (int)Math.Ceiling(profile.Completedhours.Value / 500.0)
                : null;

            // Get subscription type label
            var subscriptionLabel = profile.Subscriptiontype != null &&
                SubscriptionTypeLabels.TryGetValue(profile.Subscriptiontype.ToLower(), out var label)
                ? label
                : profile.Subscriptiontype?.ToUpper();

            // Extract degree level from education
            var degreeLevel = ExtractDegreeLevel(profile.Education);

            // Lowest active price across all subject/grade offerings (for "Từ X đ/giờ").
            var minPricePerHour = profile.Tutorsubjectgradeprices?
                .Where(p => p.Isactive)
                .Min(p => (decimal?)p.Priceperhour);

            return new TutorSearchResultResponse
            {
                TutorId = user.Userid,
                FullName = user.Fullname,
                AvatarUrl = user.Avatarurl,
                Headline = profile.Headline,
                Bio = profile.Bio,
                VideoUrl = profile.Videointrourl,
                Education = profile.Education,
                DegreeLevel = degreeLevel,
                AverageRating = profile.Averagerating,
                TotalReviews = profile.Totalreviews,
                MinPricePerHour = minPricePerHour,
                YearsOfExperience = yearsExperience,
                CompletedHours = profile.Completedhours,
                TeachingAreaCity = profile.Teachingareacity,
                TeachingAreaDistrict = profile.Teachingareadistrict,
                TeachingMode = TeachingMode.Online,
                SubscriptionType = profile.Subscriptiontype,
                SubscriptionTypeLabel = subscriptionLabel,
                Subjects = MapSubjects(profile.Tutorsubjectgradeprices),
                Certifications = null, // Not loaded in list view for performance - load in detail API
                Highlights = GenerateHighlights(profile),
                SuccessRate = CalculateSuccessRate(profile)
            };
        }

        private static string? ExtractDegreeLevel(string? education)
        {
            if (string.IsNullOrWhiteSpace(education))
                return null;

            var lowerEdu = education.ToLower();

            if (lowerEdu.Contains("phd candidate") || lowerEdu.Contains("nghiên cứu sinh"))
                return "PHD CANDIDATE";
            if (lowerEdu.Contains("phd") || lowerEdu.Contains("tiến sĩ") || lowerEdu.Contains("doctorate"))
                return "PHD";
            if (lowerEdu.Contains("m.sc") || lowerEdu.Contains("msc") || lowerEdu.Contains("thạc sĩ") || lowerEdu.Contains("master"))
                return "M.SC";
            if (lowerEdu.Contains("mba"))
                return "MBA";
            if (lowerEdu.Contains("b.sc") || lowerEdu.Contains("bsc") || lowerEdu.Contains("cử nhân") || lowerEdu.Contains("bachelor"))
                return "B.SC";

            return null;
        }

        private static List<TutorSubjectInfo>? MapSubjects(ICollection<Tutorsubjectgradeprice>? Tutorsubjectgradeprices)
        {
            if (Tutorsubjectgradeprices == null || !Tutorsubjectgradeprices.Any())
                return null;

            return Tutorsubjectgradeprices.Select(ts => new TutorSubjectInfo
            {
                SubjectId = ts.Subjectid,
                SubjectName = ts.Subject?.Subjectname,
                PricePerHour = ts.Priceperhour,
                GradeLevels = ts.Gradelevel == null ? null : new List<string> { ts.Gradelevel.Gradename },
                Tags = null
            }).ToList();
        }

        private static List<TutorCertificateInfo>? MapCertificates(ICollection<Tutorcertificate>? certificates)
        {
            if (certificates == null || !certificates.Any())
                return null;

            return certificates
                .Where(c => c.Verificationstatus != null && c.Verificationstatus.ToLower() == CertificateStatus.Verified)
                .Take(3)
                .Select(c => new TutorCertificateInfo
                {
                    CertificateName = c.Certificatename,
                    IssuingOrganization = c.Issuingorganization,
                    YearIssued = c.Yearissued
                })
                .ToList();
        }

        private static List<string>? ParseJsonArray(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                return JsonSerializer.Deserialize<List<string>>(json);
            }
            catch
            {
                return new List<string> { json };
            }
        }

        private static List<string>? GenerateHighlights(Tutorprofile profile)
        {
            var highlights = new List<string>();

            var years = profile.Completedhours.HasValue ? (int)Math.Ceiling(profile.Completedhours.Value / 500.0) : 0;
            if (years >= 5)
                highlights.Add($"Kinh nghiệm {years}+ năm giảng dạy");

            if ((profile.Totalreviews ?? 0) >= 10)
                highlights.Add($"{profile.Totalreviews}+ đánh giá tích cực");

            return highlights.Any() ? highlights : null;
        }

        private static string? CalculateSuccessRate(Tutorprofile profile)
        {
            if (profile.Averagerating.HasValue && profile.Totalreviews.HasValue && profile.Totalreviews > 0)
            {
                var successPercent = (int)Math.Round((profile.Averagerating.Value / 5.0) * 100);
                return $"{successPercent}%";
            }

            return null;
        }

        #endregion
    }
}
