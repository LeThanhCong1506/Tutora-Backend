using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Helpers;
using System.Text.Json;
using static MV.DomainLayer.Constants.LessonStatus;

namespace MV.ApplicationLayer.Services
{
    public partial class TutorVerificationService
    {
        // ─── Tutor Public Profile ─────────────────────────────────────────────

        /// <summary>
        /// Get tutor profile preview for public display (cached, requires active status)
        /// </summary>
        public async Task<TutorProfilePreviewResponse?> GetTutorProfilePreviewAsync(string tutorId)
        {
            var cacheKey = $"{CacheKeyPrefix}{tutorId}";

            // Try to get from cache first
            var cachedData = await _cache.GetStringAsync(cacheKey);
            if (!string.IsNullOrEmpty(cachedData))
            {
                return JsonSerializer.Deserialize<TutorProfilePreviewResponse>(cachedData);
            }

            // Get from database
            var profile = await _unitOfWork.TutorRepository.GetTutorProfileByIdAsync(tutorId);

            // Check if profile exists and is active
            if (profile == null || !string.Equals(profile.Profilestatus, TutorProfileStatus.Active, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var user = await _unitOfWork.UserRepository.GetUserByIdAsync(tutorId);
            var subjects = await _unitOfWork.TutorRepository.GetTutorSubjectsByTutorIdAsync(tutorId);
            var certificates = await _unitOfWork.TutorRepository.GetCertificatesByTutorIdAsync(tutorId);
            var prices = await _unitOfWork.TutorRepository.GetTutorSubjectGradePricesAsync(tutorId);

            var response = new TutorProfilePreviewResponse
            {
                // Video
                VideoIntroUrl = profile.Videointrourl,

                // Basic Info
                AvatarUrl = user?.Avatarurl,
                FullName = user?.Fullname,
                Headline = profile.Headline,
                AverageRating = profile.Averagerating,
                TotalReviews = profile.Totalreviews,
                TeachingAreaCity = profile.Teachingareacity,
                TeachingAreaDistrict = profile.Teachingareadistrict,
                TeachingMode = TeachingMode.Online,
                Subjects = subjects?.Select(s => new SubjectInfo
                {
                    SubjectId = s.Subjectid ?? 0,
                    SubjectName = s.Subject?.Subjectname,
                    GradeLevels = s.Gradelevels,
                    Tags = s.Tags
                }).ToList(),

                // Introduction
                Bio = profile.Bio,
                Education = profile.Education,
                Gpa = profile.Gpa,
                GpaScale = profile.Gpascale,
                Experience = profile.Experience,

                // Certificates
                Certificates = certificates?.Select(c => new CertificateResponse
                {
                    CertificateId = c.Certificateid,
                    CertificateName = c.Certificatename,
                    CertificateType = c.Certificatetype,
                    IssuingOrganization = c.Issuingorganization,
                    YearIssued = c.Yearissued,
                    CredentialId = c.Credentialid,
                    CredentialUrl = c.Credentialurl,
                    CertificateFileUrl = c.Certificatefileurl,
                    CreatedAt = VietnamTimeHelper.ToVietnamTime(c.Createdat ?? MV.DomainLayer.Helpers.VietnamTimeHelper.Now),
                    VerificationStatus = c.Verificationstatus,
                    VerificationNote = c.Verificationnote
                }).ToList(),

                // Pricing — removed (see SubjectGradePrices per subject)
            };

            // Cache the result
            var cacheOptions = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = CacheDuration
            };
            await _cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(response), cacheOptions);

            return response;
        }

        /// <summary>
        /// Get full tutor profile for public display (cached 20 min)
        /// Includes all profile sections + schedule + feedbacks with statistics
        /// </summary>
        public async Task<TutorFullProfileResponse?> GetTutorFullProfileAsync(string tutorId)
        {
            var cacheKey = $"{FullProfileCacheKeyPrefix}{tutorId}";

            // Try to get from cache first with short timeout to avoid blocking when Redis is down
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                var cachedData = await _cache.GetStringAsync(cacheKey, cts.Token);
                if (!string.IsNullOrEmpty(cachedData))
                {
                    return JsonSerializer.Deserialize<TutorFullProfileResponse>(cachedData);
                }
            }
            catch
            {
                // Redis not available or timeout, continue to fetch from database
            }

            // Get profile first to validate existence and active status
            var profile = await _dbContext.Tutorprofiles
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Tutorid == tutorId);

            if (profile == null || !string.Equals(profile.Profilestatus, TutorProfileStatus.Active, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // Fetch user info
            var user = await _dbContext.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Userid == tutorId);

            // Fetch subjects (include Subject entity for SubjectName)
            var subjects = await _dbContext.Tutorsubjectgradeprices
                .AsNoTracking()
                .Include(ts => ts.Subject)
                .Include(ts => ts.Gradelevel)
                .Where(ts => ts.Tutorid == tutorId)
                .ToListAsync();

            // Fetch certificates
            var certificates = await _dbContext.Tutorcertificates
                .AsNoTracking()
                .Where(c => c.Tutorid == tutorId)
                .OrderByDescending(c => c.Createdat)
                .ToListAsync();

            // Get availabilities — all slots (sorted by day and time)
            var rawAvailabilities = await _dbContext.Tutoravailabilities
                .AsNoTracking()
                .Where(a => a.Tutorid == tutorId)
                .OrderBy(a => a.Dayofweek)
                .ThenBy(a => a.Starttime)
                .ToListAsync();

            var availabilities = rawAvailabilities
                .Select(a => new TutorAvailabilityResponse
                {
                    Availabilityid = a.Availabilityid,
                    Tutorid        = a.Tutorid ?? string.Empty,
                    Dayofweek      = a.Dayofweek ?? 1,  // Default to Monday (1) instead of Sunday (0)
                    Starttime      = a.Starttime?.ToString("HH:mm") ?? string.Empty,
                    Endtime        = a.Endtime?.ToString("HH:mm") ?? string.Empty,
                    Createdat      = VietnamTimeHelper.ToVietnamTime(a.Createdat ?? MV.DomainLayer.Helpers.VietnamTimeHelper.Now)
                })
                .ToList();

            // Get packages with fixed slots (only active packages)
            var packages = await _dbContext.Tutorpackages
                .AsNoTracking()
                .Include(p => p.Tutorpackagefixedslots)
                .Where(p => p.Tutorid == tutorId && p.Isactive)
                .OrderBy(p => p.Createdat)
                .ToListAsync();

            var packageResponses = packages.Select(p => new TutorPackageResponse
            {
                PackageId = p.Packageid,
                TutorId = p.Tutorid,
                Name = p.Name,
                PackageType = p.Packagetype,
                IsActive = p.Isactive,
                FixedSlots = p.Tutorpackagefixedslots.Select(fs => new TutorPackageFixedSlotResponse
                {
                    FixedSlotId = fs.Fixedslotid,
                    DayOfWeek = fs.Dayofweek,
                    StartTime = fs.Starttime.ToString("HH:mm"),
                    EndTime = fs.Endtime.ToString("HH:mm")
                }).ToList()
            }).ToList();

            // Get feedbacks sent To this tutor (with reviewer info)
            var rawFeedbacksQuery = await _dbContext.Feedbacks
                .AsNoTracking()
                .Where(f => f.Touserid == tutorId && f.Isvisible == true)
                .OrderByDescending(f => f.Createdat)
                .Join(
                    _dbContext.Users,
                    feedback => feedback.Fromuserid,
                    fromUser => fromUser.Userid,
                    (feedback, fromUser) => new
                    {
                        FeedbackId = feedback.Feedbackid,
                        FromUserId = feedback.Fromuserid,
                        FromUserName = fromUser.Fullname,
                        FromUserAvatar = fromUser.Avatarurl,
                        Rating = feedback.Rating,
                        Comment = feedback.Comment,
                        ReplyComment = feedback.Replycomment,
                        RepliedAt = feedback.Repliedat,
                        CreatedAt = feedback.Createdat,
                        InitialGoal = feedback.InitialGoal,
                        ActualResult = feedback.ActualResult,
                        CourseDuration = feedback.CourseDuration
                    })
                .ToListAsync();

            var feedbacksQuery = rawFeedbacksQuery.Select(f => new FeedbackItemResponse
            {
                FeedbackId = f.FeedbackId,
                FromUserId = f.FromUserId,
                FromUserName = f.FromUserName,
                FromUserAvatar = f.FromUserAvatar,
                Rating = f.Rating,
                Comment = f.Comment,
                ReplyComment = f.ReplyComment,
                RepliedAt = f.RepliedAt.HasValue ? VietnamTimeHelper.ToVietnamTime(f.RepliedAt.Value) : (DateTime?)null,
                CreatedAt = f.CreatedAt.HasValue ? VietnamTimeHelper.ToVietnamTime(f.CreatedAt.Value) : (DateTime?)null,
                InitialGoal = f.InitialGoal,
                ActualResult = f.ActualResult,
                CourseDuration = f.CourseDuration
            }).ToList();

            // Calculate feedback statistics
            var totalFeedbacks = feedbacksQuery.Count;
            var averageRating = totalFeedbacks > 0
                ? feedbacksQuery.Where(f => f.Rating.HasValue).Average(f => f.Rating!.Value)
                : 0;

            // Get active classes (bookings that are paid/ongoing)
            var activeStatuses = new[] {
                BookingStatus.Paid,
                BookingStatus.DepositPaid,
                BookingStatus.Ongoing,
                BookingStatus.PendingRemainingPayment
            };

            var activeBookings = await _dbContext.Bookings
                .AsNoTracking()
                .Include(b => b.Tutorsubjectgradeprice)
                    .ThenInclude(tsgp => tsgp!.Subject)
                .Include(b => b.Student)
                .Include(b => b.Lessons)
                .Where(b => b.Tutorid == tutorId && activeStatuses.Contains(b.Status))
                .OrderByDescending(b => b.Createdat)
                .ToListAsync();

            var response = new TutorFullProfileResponse
            {
                // Video
                VideoIntroUrl = profile.Videointrourl,

                // Basic Info
                AvatarUrl = user?.Avatarurl,
                FullName = user?.Fullname,
                Headline = profile.Headline,
                TeachingAreaCity = profile.Teachingareacity,
                TeachingAreaDistrict = profile.Teachingareadistrict,
                TeachingMode = TeachingMode.Online,
                Subjects = subjects?.Select(s => new SubjectInfo
                {
                    SubjectId = s.Subjectid,
                    SubjectName = s.Subject?.Subjectname,
                    GradeLevels = s.Gradelevel?.Gradename,
                    Tags = null
                }).ToList(),

                // Introduction
                Bio = profile.Bio,
                Education = profile.Education,
                Gpa = profile.Gpa,
                GpaScale = profile.Gpascale,
                Experience = profile.Experience,

                // Certificates
                Certificates = certificates?.Select(c => new CertificateResponse
                {
                    CertificateId = c.Certificateid,
                    CertificateName = c.Certificatename,
                    CertificateType = c.Certificatetype,
                    IssuingOrganization = c.Issuingorganization,
                    YearIssued = c.Yearissued,
                    CredentialId = c.Credentialid,
                    CredentialUrl = c.Credentialurl,
                    CertificateFileUrl = c.Certificatefileurl,
                    CreatedAt = VietnamTimeHelper.ToVietnamTime(c.Createdat ?? MV.DomainLayer.Helpers.VietnamTimeHelper.Now),
                    VerificationStatus = c.Verificationstatus,
                    VerificationNote = c.Verificationnote
                }).ToList(),

                // Pricing — removed (see SubjectGradePrices per subject)

                // Schedule
                Availabilities = availabilities,

                // Packages
                Packages = packageResponses,

                // Feedback Statistics
                TotalFeedbacks = totalFeedbacks,
                AverageRating = Math.Round(averageRating, 1),

                // Feedback List
                Feedbacks = feedbacksQuery,

                // Active Classes
                TotalActiveClasses = activeBookings.Count,
                ActiveClasses = activeBookings.Select(b => new ActiveClassSummary
                {
                    BookingId = b.Bookingid,
                    SubjectName = b.Tutorsubjectgradeprice?.Subject?.Subjectname,
                    StudentName = b.Student?.Fullname,
                    TotalLessons = b.Lessons?.Count ?? 0,
                    CompletedLessons = b.Lessons?.Count(l => l.Status == Completed || l.Status == PendingConfirmation) ?? 0,
                    Status = b.Status,
                    StartDate = b.Startdate.HasValue ? VietnamTimeHelper.ToVietnamTime(b.Startdate.Value) : (DateTime?)null
                }).ToList()
            };

            // Cache the result with short timeout (fire-and-forget to not block response)
            _ = Task.Run(async () =>
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                    var cacheOptions = new DistributedCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = FullProfileCacheDuration
                    };
                    await _cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(response), cacheOptions, cts.Token);
                }
                catch
                {
                    // Redis not available or timeout, skip caching
                }
            });

            return response;
        }
    }
}
