using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.ApplicationLayer.Interfaces;
using System.Text.Json;

namespace MV.ApplicationLayer.Services
{
    public partial class TutorService : ITutorService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ISupabaseStorageService _storageService;
        private readonly IFptAiService _fptAiService;
        private readonly ICertificateVerificationService _certificateVerificationService;
        private readonly ILogger<TutorService> _logger;

        // Storage buckets
        private const string CertificateBucket = StorageBucket.CertificateFiles;
        private const string VideoBucket = StorageBucket.VideoIntroduction;
        private const string AvatarBucket = StorageBucket.TutorAvatars;

        // Certificate validation
        private static readonly string[] AllowedCertificateExtensions = { ".jpg", ".jpeg", ".png", ".pdf" };
        private const long MaxCertificateFileSize = 10 * 1024 * 1024; // 10 MB

        // Liveness threshold
        private const double LIVENESS_THRESHOLD = 85.0;

        public TutorService(
            IUnitOfWork unitOfWork,
            ISupabaseStorageService storageService,
            IFptAiService fptAiService,
            ICertificateVerificationService certificateVerificationService,
            ILogger<TutorService> logger)
        {
            _unitOfWork = unitOfWork;
            _storageService = storageService;
            _fptAiService = fptAiService;
            _certificateVerificationService = certificateVerificationService;
            _logger = logger;
        }

        // ─── Profile Queries ─────────────────────────────────────────────────

        public async Task<TutorProfileResponse?> GetTutorProfileAsync(string tutorId)
        {
            var tutorEntity = await _unitOfWork.TutorRepository.GetTutorProfileByIdAsync(tutorId);
            if (tutorEntity == null) return null;

            return new TutorProfileResponse
            {
                Headline = tutorEntity.Headline,
                Bio = tutorEntity.Bio,
                Education = tutorEntity.Education,
                Experience = tutorEntity.Experience,
                Gpa = tutorEntity.Gpa,
                GpaScale = tutorEntity.Gpascale,
                VideoIntroUrl = tutorEntity.Videointrourl,
                TeachingAreaCity = tutorEntity.Teachingareacity,
                TeachingAreaDistrict = tutorEntity.Teachingareadistrict
            };
        }

        // ─── Profile Updates ─────────────────────────────────────────────────

        public async Task<bool> UpdateTutorBasicInfoAsync(string userId, UpdateTutorBasicInfoRequest request)
        {
            var profile = await _unitOfWork.TutorRepository.GetTutorProfileByIdAsync(userId);
            if (profile == null) return false;

            // Text moderation — Headline
            if (!string.IsNullOrWhiteSpace(request.Headline))
            {
                var moderationResult = await _fptAiService.CheckTextContentSafeAsync(request.Headline);
                if (!moderationResult.IsSafe)
                {
                    var violations = moderationResult.Violations?.Select(v => v.Category).Distinct();
                    throw new ArgumentException($"Headline chứa nội dung không phù hợp: {string.Join(", ", violations ?? new[] { "vi phạm chính sách" })}");
                }
            }

            profile.Headline = request.Headline;
            profile.Teachingareacity = request.TeachingAreaCity;
            profile.Teachingareadistrict = request.TeachingAreaDistrict;
            profile.Updatedat = MV.DomainLayer.Helpers.VietnamTimeHelper.Now;



            await _unitOfWork.SaveChangesAsync();
            await TryAutoActivateProfileAsync(userId);
            return true;
        }

        public async Task<bool> UpdateTutorIntroductionAsync(string userId, UpdateTutorIntroductionRequest request)
        {
            var profile = await _unitOfWork.TutorRepository.GetTutorProfileByIdAsync(userId);
            if (profile == null) return false;

            if (request.Gpa > request.GpaScale)
            {
                throw new ArgumentException($"GPA ({request.Gpa}) cannot exceed GPA Scale ({request.GpaScale})");
            }

            // Text moderation — Bio
            if (!string.IsNullOrWhiteSpace(request.Bio))
            {
                var bioResult = await _fptAiService.CheckTextContentSafeAsync(request.Bio);
                if (!bioResult.IsSafe)
                {
                    var violations = bioResult.Violations?.Select(v => v.Category).Distinct();
                    throw new ArgumentException($"Bio chứa nội dung không phù hợp: {string.Join(", ", violations ?? new[] { "vi phạm chính sách" })}");
                }
            }

            // Text moderation — Experience
            if (!string.IsNullOrWhiteSpace(request.Experience))
            {
                var expResult = await _fptAiService.CheckTextContentSafeAsync(request.Experience);
                if (!expResult.IsSafe)
                {
                    var violations = expResult.Violations?.Select(v => v.Category).Distinct();
                    throw new ArgumentException($"Experience chứa nội dung không phù hợp: {string.Join(", ", violations ?? new[] { "vi phạm chính sách" })}");
                }
            }

            profile.Bio = request.Bio;
            profile.Education = request.Education;
            profile.Gpascale = request.GpaScale;
            profile.Gpa = request.Gpa;
            profile.Experience = request.Experience;
            profile.Updatedat = MV.DomainLayer.Helpers.VietnamTimeHelper.Now;

            await _unitOfWork.SaveChangesAsync();
            await TryAutoActivateProfileAsync(userId);
            return true;
        }

        public async Task<bool> UpdateTutorSubjectsAsync(string userId, UpdateTutorSubjectsRequest request)
        {
            var profile = await _unitOfWork.TutorRepository.GetTutorProfileByIdAsync(userId);
            if (profile == null) return false;

            await ValidateSubjectGradePricesAsync(request.SubjectGradePrices);

            profile.Updatedat = MV.DomainLayer.Helpers.VietnamTimeHelper.Now;

            await _unitOfWork.TutorRepository.ReplaceTutorSubjectGradePricesAsync(
                userId,
                MapSubjectGradePriceRequests(userId, request.SubjectGradePrices));

            await _unitOfWork.SaveChangesAsync();
            await TryAutoActivateProfileAsync(userId);
            return true;
        }

        // ─── Pricing ─────────────────────────────────────────────────────────

        public async Task<TutorPricingResponse?> GetTutorPricingAsync(string tutorId)
        {
            var profile = await _unitOfWork.TutorRepository.GetTutorProfileByIdAsync(tutorId);
            if (profile == null) return null;

            return new TutorPricingResponse
            {
                SubjectGradePrices = profile.Tutorsubjectgradeprices.Select(MapSubjectGradePriceResponse).ToList()
            };
        }

        public async Task<bool> UpdateTutorPricingAsync(string tutorId, UpdateTutorPricingRequest request)
        {
            var profile = await _unitOfWork.TutorRepository.GetTutorProfileByIdAsync(tutorId);
            if (profile == null) return false;

            if (!request.SubjectGradePrices.Any())
            {
                throw new ArgumentException("Cần ít nhất một giá theo môn và lớp");
            }

            await ValidateSubjectGradePricesAsync(request.SubjectGradePrices);

            profile.Updatedat = MV.DomainLayer.Helpers.VietnamTimeHelper.Now;

            await _unitOfWork.TutorRepository.ReplaceTutorSubjectGradePricesAsync(
                tutorId,
                MapSubjectGradePriceRequests(tutorId, request.SubjectGradePrices));

            await _unitOfWork.SaveChangesAsync();
            await TryAutoActivateProfileAsync(tutorId);
            return true;
        }

        // ─── Packages ────────────────────────────────────────────────────────

        public async Task<List<TutorPackageResponse>> GetTutorPackagesAsync(string tutorId, bool includeInactive = false)
        {
            var packages = await _unitOfWork.TutorRepository.GetTutorPackagesAsync(tutorId, includeInactive);
            return packages.Select(MapTutorPackageResponse).ToList();
        }

        public async Task<TutorPackageResponse?> CreateTutorPackageAsync(string tutorId, CreateTutorPackageRequest request)
        {
            var profile = await _unitOfWork.TutorRepository.GetTutorProfileByIdAsync(tutorId);
            if (profile == null) return null;

            ValidateTutorPackageRequest(request);

            var now = MV.DomainLayer.Helpers.VietnamTimeHelper.Now;
            var package = new Tutorpackage
            {
                Tutorid = tutorId,
                Name = request.Name.Trim(),
                Packagetype = request.PackageType,
                Durationminutespersession = request.DurationMinutesPerSession,
                Description = request.Description,
                Isactive = true,
                Createdat = now,
                Updatedat = now,
                Tutorpackagefixedslots = request.FixedSlots.Select(s => new Tutorpackagefixedslot
                {
                    Dayofweek = s.DayOfWeek,
                    Starttime = TimeOnly.Parse(s.StartTime),
                    Endtime = TimeOnly.Parse(s.EndTime),
                    Createdat = now
                }).ToList()
            };

            await _unitOfWork.TutorRepository.AddTutorPackageAsync(package);
            await _unitOfWork.SaveChangesAsync();

            return MapTutorPackageResponse(package);
        }

        public async Task<bool> DeactivateTutorPackageAsync(string tutorId, int packageId)
        {
            var package = await _unitOfWork.TutorRepository.GetTutorPackageAsync(tutorId, packageId);
            if (package == null) return false;

            package.Isactive = false;
            package.Updatedat = MV.DomainLayer.Helpers.VietnamTimeHelper.Now;
            await _unitOfWork.SaveChangesAsync();
            return true;
        }

        // ─── Status Management ────────────────────────────────────────────────

        /// <summary>Tutor manually submits profile for admin review.</summary>
        public async Task<bool> SubmitForAdminReviewAsync(string tutorId)
        {
            var profile = await _unitOfWork.TutorRepository.GetTutorProfileByIdAsync(tutorId);
            if (profile == null) return false;

            if (!string.Equals(profile.Profilestatus, TutorProfileStatus.Draft, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(profile.Profilestatus, TutorProfileStatus.Rejected, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Cannot submit for review. Current status is '{profile.Profilestatus}'.");
            }

            profile.Profilestatus = TutorProfileStatus.PendingApproval;
            profile.Updatedat = MV.DomainLayer.Helpers.VietnamTimeHelper.Now;

            await _unitOfWork.SaveChangesAsync();
            _logger.LogInformation("Profile {TutorId} submitted for admin review", tutorId);
            return true;
        }

        /// <summary>Check and auto-activate profile if all conditions are met.</summary>
        public async Task<bool> TryAutoActivateProfileAsync(string tutorId)
        {
            var profile = await _unitOfWork.TutorRepository.GetTutorProfileByIdAsync(tutorId);
            var user = await _unitOfWork.UserRepository.GetUserByIdAsync(tutorId);
            var subjects = await _unitOfWork.TutorRepository.GetTutorSubjectsByTutorIdAsync(tutorId);
            var prices = await _unitOfWork.TutorRepository.GetTutorSubjectGradePricesAsync(tutorId);
            var certificates = await _unitOfWork.TutorRepository.GetCertificatesByTutorIdAsync(tutorId);

            if (profile == null || user == null) return false;

            // Only check when still in Draft
            if (!string.Equals(profile.Profilestatus, TutorProfileStatus.Draft, StringComparison.OrdinalIgnoreCase)) return false;

            bool identityVerified = user.Isidentityverified ?? false;
            bool hasRequiredFields = CheckRequiredFields(profile, user, subjects, prices);

            bool hasVerifiedCertificate = certificates != null &&
                certificates.Any(c => string.Equals(c.Verificationstatus, CertificateStatus.Verified, StringComparison.OrdinalIgnoreCase));

            if (identityVerified && hasRequiredFields && hasVerifiedCertificate)
            {
                profile.Profilestatus = TutorProfileStatus.Active;
                profile.Ispublic = true;
                profile.Updatedat = MV.DomainLayer.Helpers.VietnamTimeHelper.Now;
                await _unitOfWork.SaveChangesAsync();

                _logger.LogInformation("Profile {TutorId} auto-activated", tutorId);
                return true;
            }

            return false;
        }

        // ─── Private helpers ─────────────────────────────────────────────────

        private static bool CheckRequiredFields(
            Tutorprofile profile,
            User user,
            List<Tutorsubject>? subjects,
            List<Tutorsubjectgradeprice>? prices)
        {
            return !string.IsNullOrWhiteSpace(profile.Headline) &&
                   !string.IsNullOrWhiteSpace(profile.Teachingareacity) &&
                   subjects != null && subjects.Count > 0 &&
                   !string.IsNullOrWhiteSpace(profile.Bio) &&
                   !string.IsNullOrWhiteSpace(profile.Education) &&
                   prices != null && prices.Any(p => p.Isactive && p.Priceperhour > 0) &&
                   !string.IsNullOrWhiteSpace(user.Avatarurl);
        }

        private async Task ValidateSubjectGradePricesAsync(List<TutorSubjectGradePriceRequest> prices)
        {
            if (!prices.Any())
            {
                throw new ArgumentException("Cần ít nhất một giá theo môn và lớp");
            }

            var invalidPrices = prices.Where(p => p.PricePerHour < 50000 || p.PricePerHour > 2000000).ToList();
            if (invalidPrices.Any())
            {
                throw new ArgumentException("Giá theo giờ phải nằm trong khoảng 50,000 - 2,000,000 VND");
            }

            var duplicate = prices
                .GroupBy(p => new { p.SubjectId, p.GradeLevelId })
                .FirstOrDefault(g => g.Count() > 1);
            if (duplicate != null)
            {
                throw new ArgumentException($"Trùng giá cho subjectId={duplicate.Key.SubjectId}, gradeLevelId={duplicate.Key.GradeLevelId}");
            }

            var subjectIds = prices.Select(p => p.SubjectId).Distinct().ToList();
            var existingSubjectIds = await _unitOfWork.TutorRepository.GetExistingSubjectIdsAsync(subjectIds);
            var invalidSubjectIds = subjectIds.Except(existingSubjectIds).ToList();
            if (invalidSubjectIds.Any())
            {
                throw new ArgumentException($"Subject IDs không tồn tại: {string.Join(", ", invalidSubjectIds)}");
            }

            var gradeLevelIds = prices.Select(p => p.GradeLevelId).Distinct().ToList();
            var existingGradeLevelIds = await _unitOfWork.TutorRepository.GetExistingGradeLevelIdsAsync(gradeLevelIds);
            var invalidGradeLevelIds = gradeLevelIds.Except(existingGradeLevelIds).ToList();
            if (invalidGradeLevelIds.Any())
            {
                throw new ArgumentException($"GradeLevel IDs không tồn tại: {string.Join(", ", invalidGradeLevelIds)}");
            }
        }

        private static IEnumerable<Tutorsubjectgradeprice> MapSubjectGradePriceRequests(
            string tutorId,
            IEnumerable<TutorSubjectGradePriceRequest> prices)
        {
            return prices.Select(p => new Tutorsubjectgradeprice
            {
                Tutorid = tutorId,
                Subjectid = p.SubjectId,
                Gradelevelid = p.GradeLevelId,
                Priceperhour = p.PricePerHour,
                Currency = string.IsNullOrWhiteSpace(p.Currency) ? "VND" : p.Currency!,
                Isactive = p.IsActive
            });
        }

        private static TutorSubjectGradePriceResponse MapSubjectGradePriceResponse(Tutorsubjectgradeprice price)
        {
            return new TutorSubjectGradePriceResponse
            {
                Id = price.Id,
                SubjectId = price.Subjectid,
                SubjectName = price.Subject?.Subjectname,
                GradeLevelId = price.Gradelevelid,
                GradeLevelName = price.Gradelevel?.Gradename,
                PricePerHour = price.Priceperhour,
                Currency = price.Currency,
                IsActive = price.Isactive
            };
        }

        private static void ValidateTutorPackageRequest(CreateTutorPackageRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                throw new ArgumentException("Tên package là bắt buộc");
            }

            if (request.PackageType != Tutorpackage.FlexiblePackageType && request.PackageType != Tutorpackage.FixedPackageType)
            {
                throw new ArgumentException("PackageType phải là 1 (flexible) hoặc 2 (fixed)");
            }

            if (request.DurationMinutesPerSession <= 0)
            {
                throw new ArgumentException("Thời lượng mỗi buổi phải lớn hơn 0 phút");
            }

            if (request.PackageType == Tutorpackage.FixedPackageType && !request.FixedSlots.Any())
            {
                throw new ArgumentException("Package fixed phải có ít nhất một fixed slot");
            }

            if (request.PackageType == Tutorpackage.FlexiblePackageType && request.FixedSlots.Any())
            {
                throw new ArgumentException("Package flexible không dùng fixed slots");
            }

            foreach (var slot in request.FixedSlots)
            {
                if (slot.DayOfWeek < 0 || slot.DayOfWeek > 6)
                {
                    throw new ArgumentException("DayOfWeek phải nằm trong khoảng 0-6");
                }

                if (!TimeOnly.TryParse(slot.StartTime, out var start) || !TimeOnly.TryParse(slot.EndTime, out var end))
                {
                    throw new ArgumentException("StartTime/EndTime phải đúng định dạng HH:mm");
                }

                if (start >= end)
                {
                    throw new ArgumentException("StartTime phải trước EndTime");
                }

                if ((int)(end - start).TotalMinutes != request.DurationMinutesPerSession)
                {
                    throw new ArgumentException("Thời lượng fixed slot phải bằng DurationMinutesPerSession");
                }
            }
        }

        private static TutorPackageResponse MapTutorPackageResponse(Tutorpackage package)
        {
            return new TutorPackageResponse
            {
                PackageId = package.Packageid,
                TutorId = package.Tutorid,
                Name = package.Name,
                PackageType = package.Packagetype,
                DurationMinutesPerSession = package.Durationminutespersession,
                Description = package.Description,
                IsActive = package.Isactive,
                FixedSlots = package.Tutorpackagefixedslots
                    .OrderBy(s => s.Dayofweek)
                    .ThenBy(s => s.Starttime)
                    .Select(s => new TutorPackageFixedSlotResponse
                    {
                        FixedSlotId = s.Fixedslotid,
                        DayOfWeek = s.Dayofweek,
                        StartTime = s.Starttime.ToString("HH:mm"),
                        EndTime = s.Endtime.ToString("HH:mm")
                    })
                    .ToList()
            };
        }
    }
}
