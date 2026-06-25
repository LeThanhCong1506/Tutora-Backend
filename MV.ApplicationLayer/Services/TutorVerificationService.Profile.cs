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
        private static readonly string[] PaidBookingStatuses =
        [
            BookingStatus.DepositPaid,
            BookingStatus.PendingRemainingPayment,
            BookingStatus.Paid,
            BookingStatus.Ongoing,
            BookingStatus.Completed
        ];

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
                    CreatedAt = c.Createdat ?? MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow,
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
        /// Get tutor profile info without schedule/package data.
        /// </summary>
        public async Task<TutorProfileInfoResponse?> GetTutorProfileInfoAsync(string tutorId)
        {
            var profile = await _dbContext.Tutorprofiles
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Tutorid == tutorId);

            if (!IsActiveTutorProfile(profile))
            {
                return null;
            }

            var user = await _dbContext.Users
                .AsNoTracking()
                .Where(u => u.Userid == tutorId)
                .Select(u => new
                {
                    u.Avatarurl,
                    u.Fullname
                })
                .FirstOrDefaultAsync();

            var subjects = await _dbContext.Tutorsubjectgradeprices
                .AsNoTracking()
                .Where(ts => ts.Tutorid == tutorId)
                .Select(ts => new
                {
                    ts.Id,
                    ts.Subjectid,
                    SubjectName = ts.Subject != null ? ts.Subject.Subjectname : null,
                    ts.Gradelevelid,
                    GradeLevelName = ts.Gradelevel != null ? ts.Gradelevel.Gradename : null,
                    ts.Priceperhour,
                    ts.Durationminutespersession,
                    ts.Sessionsperweek,
                    ts.Currency,
                    ts.Isactive
                })
                .ToListAsync();

            var certificates = await _dbContext.Tutorcertificates
                .AsNoTracking()
                .Where(c => c.Tutorid == tutorId && c.Verificationstatus == CertificateStatus.Verified)
                .OrderByDescending(c => c.Createdat)
                .Select(c => new CertificateResponse
                {
                    CertificateId = c.Certificateid,
                    CertificateName = c.Certificatename,
                    CertificateType = c.Certificatetype,
                    IssuingOrganization = c.Issuingorganization,
                    YearIssued = c.Yearissued,
                    CredentialId = c.Credentialid,
                    CredentialUrl = c.Credentialurl,
                    CertificateFileUrl = c.Certificatefileurl,
                    CreatedAt = c.Createdat ?? MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow,
                    VerificationStatus = c.Verificationstatus,
                    VerificationNote = c.Verificationnote
                })
                .ToListAsync();

            var response = new TutorProfileInfoResponse
            {
                VideoIntroUrl = profile!.Videointrourl,
                AvatarUrl = user?.Avatarurl,
                FullName = user?.Fullname,
                Headline = profile.Headline,
                TeachingAreaCity = profile.Teachingareacity,
                TeachingAreaDistrict = profile.Teachingareadistrict,
                TeachingMode = TeachingMode.Online,
                SubjectGradePrices = subjects
                    .Where(s => s.Isactive)
                    .Select(MapSubjectGradePrice)
                    .ToList(),
                Bio = profile.Bio,
                Education = profile.Education,
                Gpa = profile.Gpa,
                GpaScale = profile.Gpascale,
                Experience = profile.Experience,
                Certificates = certificates,
                TotalFeedbacks = profile.Totalreviews ?? 0,
                AverageRating = Math.Round(profile.Averagerating ?? 0, 1)
            };

            return response;
        }

        /// <summary>
        /// Get tutor schedule including weekly availability and packages.
        /// </summary>
        public async Task<TutorScheduleResponse?> GetTutorScheduleAsync(string tutorId)
        {
            var profile = await _dbContext.Tutorprofiles
                .AsNoTracking()
                .Where(t => t.Tutorid == tutorId)
                .Select(t => new
                {
                    t.Tutorid,
                    t.Profilestatus
                })
                .FirstOrDefaultAsync();

            if (!IsActiveTutorProfile(profile))
            {
                return null;
            }

            var availabilities = await _dbContext.Tutoravailabilities
                .AsNoTracking()
                .Where(a => a.Tutorid == tutorId)
                .OrderBy(a => a.Dayofweek)
                .ThenBy(a => a.Starttime)
                .Select(a => new TutorAvailabilityResponse
                {
                    Availabilityid = a.Availabilityid,
                    Tutorid = a.Tutorid ?? string.Empty,
                    Dayofweek = a.Dayofweek ?? 1,
                    Starttime = a.Starttime != null ? a.Starttime.ToString() : string.Empty,
                    Endtime = a.Endtime != null ? a.Endtime.ToString() : string.Empty,
                    Createdat = a.Createdat ?? MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
                })
                .ToListAsync();

            var packages = await _dbContext.Tutorpackages
                .AsNoTracking()
                .Where(p => p.Tutorid == tutorId && p.Isactive)
                .OrderBy(p => p.Createdat)
                .Select(p => new TutorPackageResponse
                {
                    PackageId = p.Packageid,
                    TutorId = p.Tutorid,
                    Name = p.Name,
                    PackageType = p.Packagetype,
                    IsActive = p.Isactive,
                    FixedSlots = p.Tutorpackagefixedslots
                        .OrderBy(fs => fs.Dayofweek)
                        .ThenBy(fs => fs.Starttime)
                        .Select(fs => new TutorPackageFixedSlotResponse
                        {
                            FixedSlotId = fs.Fixedslotid,
                            DayOfWeek = fs.Dayofweek,
                            StartTime = fs.Starttime.ToString(),
                            EndTime = fs.Endtime.ToString()
                        })
                        .ToList()
                })
                .ToListAsync();

            var response = new TutorScheduleResponse
            {
                TutorId = tutorId,
                Availabilities = availabilities,
                Packages = packages
            };

            return response;
        }

        /// <summary>
        /// Get full tutor profile for public display (cached 20 min)
        /// Includes all profile sections + schedule + feedbacks with statistics
        /// </summary>
        public async Task<TutorFullProfileResponse?> GetTutorFullProfileAsync(string tutorId)
        {
            var cacheKey = $"{FullProfileCacheKeyPrefix}{tutorId}";
            var cachedResponse = await TryGetCachedResponseAsync<TutorFullProfileResponse>(cacheKey);
            if (cachedResponse != null)
            {
                return cachedResponse;
            }

            var profileInfo = await GetTutorProfileInfoAsync(tutorId);
            if (profileInfo == null)
            {
                return null;
            }

            var schedule = await GetTutorScheduleAsync(tutorId);
            if (schedule == null)
            {
                return null;
            }

            var feedbacks = await GetTutorFeedbacksAsync(tutorId);

            var totalLessons = await _dbContext.Lessons
                .AsNoTracking()
                .CountAsync(l => l.Tutorid == tutorId
                    && l.Status != LessonStatus.Cancelled
                    && l.Status != LessonStatus.CancelledNoshow
                    && l.Booking != null && PaidBookingStatuses.Contains(l.Booking.Status!));

            var totalStudents = await _dbContext.Bookings
                .AsNoTracking()
                .Where(b => b.Tutorid == tutorId && PaidBookingStatuses.Contains(b.Status!))
                .Select(b => b.Studentid)
                .Distinct()
                .CountAsync();

            var totalFeedbacks = feedbacks.Count;
            var averageRating = totalFeedbacks > 0
                ? feedbacks.Where(f => f.Rating.HasValue).Average(f => f.Rating!.Value)
                : 0;

            var response = new TutorFullProfileResponse
            {
                VideoIntroUrl = profileInfo.VideoIntroUrl,
                AvatarUrl = profileInfo.AvatarUrl,
                FullName = profileInfo.FullName,
                Headline = profileInfo.Headline,
                TeachingAreaCity = profileInfo.TeachingAreaCity,
                TeachingAreaDistrict = profileInfo.TeachingAreaDistrict,
                TeachingMode = profileInfo.TeachingMode,
                SubjectGradePrices = profileInfo.SubjectGradePrices,
                Bio = profileInfo.Bio,
                Education = profileInfo.Education,
                Gpa = profileInfo.Gpa,
                GpaScale = profileInfo.GpaScale,
                Experience = profileInfo.Experience,
                Certificates = profileInfo.Certificates,
                Availabilities = schedule.Availabilities,
                Packages = schedule.Packages,
                TotalFeedbacks = totalFeedbacks,
                AverageRating = Math.Round(averageRating, 1),
                Feedbacks = feedbacks,
                TotalLessons = totalLessons,
                TotalStudents = totalStudents
            };

            CacheResponseWithoutBlocking(cacheKey, response, FullProfileCacheDuration);
            return response;
        }

        private static bool IsActiveTutorProfile(dynamic profile)
        {
            return profile != null &&
                   string.Equals(profile.Profilestatus, TutorProfileStatus.Active, StringComparison.OrdinalIgnoreCase);
        }

        private async Task<List<FeedbackItemResponse>> GetTutorFeedbacksAsync(string tutorId)
        {
            var rawFeedbacks = await _dbContext.Feedbacks
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

            return rawFeedbacks.Select(f => new FeedbackItemResponse
            {
                FeedbackId = f.FeedbackId,
                FromUserId = f.FromUserId,
                FromUserName = f.FromUserName,
                FromUserAvatar = f.FromUserAvatar,
                Rating = f.Rating,
                Comment = f.Comment,
                ReplyComment = f.ReplyComment,
                RepliedAt = f.RepliedAt,
                CreatedAt = f.CreatedAt,
                InitialGoal = f.InitialGoal,
                ActualResult = f.ActualResult,
                CourseDuration = f.CourseDuration
            }).ToList();
        }

        private static SubjectInfo MapSubjectInfo(dynamic subject)
        {
            return new SubjectInfo
            {
                SubjectId = subject.Subjectid,
                SubjectName = subject.SubjectName,
                GradeLevels = subject.GradeLevelName,
                Tags = null
            };
        }

        private static TutorSubjectGradePriceResponse MapSubjectGradePrice(dynamic subject)
        {
            return new TutorSubjectGradePriceResponse
            {
                Id = subject.Id,
                SubjectId = subject.Subjectid,
                SubjectName = subject.SubjectName,
                GradeLevelId = subject.Gradelevelid,
                GradeLevelName = subject.GradeLevelName,
                PricePerHour = subject.Priceperhour,
                DurationMinutesPerSession = subject.Durationminutespersession,
                SessionsPerWeek = subject.Sessionsperweek,
                Currency = subject.Currency,
                IsActive = subject.Isactive
            };
        }

        private async Task<T?> TryGetCachedResponseAsync<T>(string cacheKey) where T : class
        {
            try
            {
                using var cts = new CancellationTokenSource(CacheOperationTimeout);
                var cachedData = await _cache.GetStringAsync(cacheKey, cts.Token);
                if (!string.IsNullOrEmpty(cachedData))
                {
                    return JsonSerializer.Deserialize<T>(cachedData);
                }
            }
            catch
            {
                // Redis not available or timeout, continue to fetch from database
            }

            return null;
        }

        private void CacheResponseWithoutBlocking<T>(string cacheKey, T response, TimeSpan duration)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    using var cts = new CancellationTokenSource(CacheOperationTimeout);
                    var cacheOptions = new DistributedCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = duration
                    };
                    await _cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(response), cacheOptions, cts.Token);
                }
                catch
                {
                    // Redis not available or timeout, skip caching
                }
            });
        }
    }
}
