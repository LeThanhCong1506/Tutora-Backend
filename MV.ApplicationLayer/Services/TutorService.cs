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
        private readonly IFileStorageService _storageService;
        private readonly IFptAiService _fptAiService;
        private readonly INotificationService _notificationService;
        private readonly ILogger<TutorService> _logger;

        // Storage buckets
        private const string CertificateBucket = StorageBucket.CertificateFiles;
        private const string VideoBucket = StorageBucket.VideoIntroduction;
        private const string AvatarBucket = StorageBucket.TutorAvatars;
        private const string CccdBucket = StorageBucket.CccdFiles;

        // Certificate validation
        private static readonly string[] AllowedCertificateExtensions = { ".jpg", ".jpeg", ".png", ".pdf" };
        private const long MaxCertificateFileSize = 10 * 1024 * 1024; // 10 MB

        public TutorService(
            IUnitOfWork unitOfWork,
            IFileStorageService storageService,
            IFptAiService fptAiService,
            INotificationService notificationService,
            ILogger<TutorService> logger)
        {
            _unitOfWork = unitOfWork;
            _storageService = storageService;
            _fptAiService = fptAiService;
            _notificationService = notificationService;
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
            profile.Updatedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;



            await _unitOfWork.SaveChangesAsync();
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
            profile.Updatedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

            await _unitOfWork.SaveChangesAsync();
            return true;
        }

        public async Task<bool> UpdateTutorSubjectsAsync(string userId, UpdateTutorSubjectsRequest request)
        {
            var profile = await _unitOfWork.TutorRepository.GetTutorProfileByIdAsync(userId);
            if (profile == null) return false;

            await ValidateSubjectGradePricesAsync(userId, request.SubjectGradePrices);

            profile.Updatedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

            await _unitOfWork.TutorRepository.ReplaceTutorSubjectGradePricesAsync(
                userId,
                MapSubjectGradePriceRequests(userId, request.SubjectGradePrices));

            await _unitOfWork.SaveChangesAsync();
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

            await ValidateSubjectGradePricesAsync(tutorId, request.SubjectGradePrices);

            profile.Updatedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

            await _unitOfWork.TutorRepository.ReplaceTutorSubjectGradePricesAsync(
                tutorId,
                MapSubjectGradePriceRequests(tutorId, request.SubjectGradePrices));

            await _unitOfWork.SaveChangesAsync();
            return true;
        }

        public async Task<TutorSubjectGradePriceResponse> AddSubjectGradePriceAsync(string tutorId, TutorSubjectGradePriceRequest request)
        {
            var profile = await _unitOfWork.TutorRepository.GetTutorProfileByIdAsync(tutorId);
            if (profile == null)
            {
                throw new ArgumentException("Không tìm thấy hồ sơ gia sư");
            }

            // Validate single price entry
            await ValidateSubjectGradePricesAsync(tutorId, new List<TutorSubjectGradePriceRequest> { request });

            // Check if already exists
            var existing = await _unitOfWork.TutorRepository.GetTutorSubjectGradePriceAsync(
                tutorId, request.SubjectId, request.GradeLevelId);
            
            if (existing != null)
            {
                throw new ArgumentException($"Đã tồn tại giá cho môn {request.SubjectId} và khối {request.GradeLevelId}. Vui lòng dùng PUT để cập nhật.");
            }

            // Create new price entry
            var newPrice = new Tutorsubjectgradeprice
            {
                Tutorid = tutorId,
                Subjectid = request.SubjectId,
                Gradelevelid = request.GradeLevelId,
                Priceperhour = request.PricePerHour,
                Durationminutespersession = request.DurationMinutesPerSession,
                Sessionsperweek = request.SessionsPerWeek,
                Currency = string.IsNullOrWhiteSpace(request.Currency) ? "VND" : request.Currency!,
                Isactive = request.IsActive
            };

            await _unitOfWork.TutorRepository.AddTutorSubjectGradePriceAsync(newPrice);
            profile.Updatedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
            
            await _unitOfWork.SaveChangesAsync();

            // Reload to get navigation properties
            var created = await _unitOfWork.TutorRepository.GetTutorSubjectGradePriceAsync(
                tutorId, request.SubjectId, request.GradeLevelId);

            return MapSubjectGradePriceResponse(created!);
        }

        public async Task<bool> DeleteSubjectGradePriceAsync(string tutorId, int subjectId, int gradeLevelId)
        {
            var profile = await _unitOfWork.TutorRepository.GetTutorProfileByIdAsync(tutorId);
            if (profile == null)
                throw new ArgumentException("Không tìm thấy hồ sơ gia sư.");

            var deleted = await _unitOfWork.TutorRepository.DeleteTutorSubjectGradePriceAsync(tutorId, subjectId, gradeLevelId);
            if (!deleted)
                return false;

            profile.Updatedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
            await _unitOfWork.SaveChangesAsync();
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

            var now = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
            var package = new Tutorpackage
            {
                Tutorid = tutorId,
                Name = request.Name.Trim(),
                Packagetype = request.PackageType,
                Isactive = true,
                Createdat = now,
                Updatedat = now,
                Tutorpackagefixedslots = request.FixedSlots.Select(s =>
                {
                    // FE sends UTC — use directly, no conversion needed
                    return new Tutorpackagefixedslot
                    {
                        Dayofweek = s.DayOfWeek,
                        Starttime = TimeOnly.Parse(s.StartTime),
                        Endtime   = TimeOnly.Parse(s.EndTime),
                        Createdat = now
                    };
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
            package.Updatedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
            await _unitOfWork.SaveChangesAsync();
            return true;
        }

        // ─── Status Management ────────────────────────────────────────────────

        /// <summary>
        /// Trả về trạng thái hoàn thành 6 mục hồ sơ gia sư.
        /// FE dùng để hiển thị progress bar và bật/tắt nút Submit.
        /// </summary>
        public async Task<ProfileCompletionResponse> GetProfileCompletionAsync(string tutorId)
        {
            var profile      = await _unitOfWork.TutorRepository.GetTutorProfileByIdAsync(tutorId);
            var user         = await _unitOfWork.UserRepository.GetUserByIdAsync(tutorId);
            var subjects     = await _unitOfWork.TutorRepository.GetTutorSubjectsByTutorIdAsync(tutorId);
            var prices       = await _unitOfWork.TutorRepository.GetTutorSubjectGradePricesAsync(tutorId);
            var certificates = await _unitOfWork.TutorRepository.GetCertificatesByTutorIdAsync(tutorId);
            var availabilities = await _unitOfWork.TutorRepository.GetAvailabilitiesByTutorIdAsync(tutorId);

            bool hasBasicInfo    = profile != null
                                   && !string.IsNullOrWhiteSpace(profile.Headline)
                                   && !string.IsNullOrWhiteSpace(profile.Teachingareacity);
            bool hasIntroduction = profile != null
                                   && !string.IsNullOrWhiteSpace(profile.Bio)
                                   && !string.IsNullOrWhiteSpace(profile.Education);
            bool hasSubjects     = subjects != null && subjects.Count > 0
                                   && prices != null && prices.Any(p => p.Isactive && p.Priceperhour > 0);
            bool hasAvailability = availabilities != null && availabilities.Count > 0;
            bool hasIdentity     = user?.Isidentityverified ?? false;
            bool hasCertificate  = certificates != null
                                   && certificates.Any(c => string.Equals(
                                       c.Verificationstatus, CertificateStatus.Verified,
                                       StringComparison.OrdinalIgnoreCase));

            var sections = new List<ProfileSection>
            {
                new() { Key = "basic_info",    Name = "Thông tin cơ bản",          IsComplete = hasBasicInfo,
                    MissingReason = hasBasicInfo    ? null : "Cần điền Tiêu đề và Khu vực dạy học" },
                new() { Key = "introduction",  Name = "Giới thiệu bản thân",        IsComplete = hasIntroduction,
                    MissingReason = hasIntroduction ? null : "Cần điền Bio và Học vấn" },
                new() { Key = "subjects",      Name = "Môn học & Bảng giá",         IsComplete = hasSubjects,
                    MissingReason = hasSubjects     ? null : "Cần chọn ít nhất 1 môn học và thiết lập giá" },
                new() { Key = "availability",  Name = "Lịch rảnh",                  IsComplete = hasAvailability,
                    MissingReason = hasAvailability ? null : "Cần thiết lập ít nhất 1 khung giờ rảnh" },
                new() { Key = "identity",      Name = "Xác minh CCCD",              IsComplete = hasIdentity,
                    MissingReason = hasIdentity     ? null : "Cần upload CCCD và chờ xác minh" },
                new() { Key = "certificate",   Name = "Chứng chỉ",                  IsComplete = hasCertificate,
                    MissingReason = hasCertificate  ? null : "Cần ít nhất 1 chứng chỉ được xác minh" },
            };

            int completedCount = sections.Count(s => s.IsComplete);
            bool canSubmit = completedCount == 6
                             && profile != null
                             && (string.Equals(profile.Profilestatus, TutorProfileStatus.Draft,    StringComparison.OrdinalIgnoreCase)
                              || string.Equals(profile.Profilestatus, TutorProfileStatus.Rejected, StringComparison.OrdinalIgnoreCase));

            return new ProfileCompletionResponse
            {
                CompletedSections = completedCount,
                TotalSections     = 6,
                CanSubmit         = canSubmit,
                ProfileStatus     = profile?.Profilestatus ?? TutorProfileStatus.Draft,
                Sections          = sections
            };
        }

        /// <summary>
        /// Tutor nộp hồ sơ để admin/staff xét duyệt.
        /// Chỉ được phép khi đã hoàn thành đủ 6 mục và đang ở trạng thái Draft hoặc Rejected.
        /// </summary>
        public async Task<bool> SubmitForAdminReviewAsync(string tutorId)
        {
            var completion = await GetProfileCompletionAsync(tutorId);

            if (completion.CompletedSections < 6)
            {
                var missing = completion.Sections
                    .Where(s => !s.IsComplete)
                    .Select(s => s.Name)
                    .ToList();
                throw new InvalidOperationException(
                    $"Hồ sơ chưa hoàn chỉnh ({completion.CompletedSections}/6). " +
                    $"Các mục còn thiếu: {string.Join(", ", missing)}.");
            }

            if (!completion.CanSubmit)
            {
                throw new InvalidOperationException(
                    $"Không thể nộp hồ sơ ở trạng thái hiện tại ({completion.ProfileStatus}).");
            }

            var profile = await _unitOfWork.TutorRepository.GetTutorProfileByIdAsync(tutorId);
            profile!.Profilestatus = TutorProfileStatus.PendingApproval;
            profile.Updatedat      = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

            await _unitOfWork.SaveChangesAsync();
            _logger.LogInformation("Profile {TutorId} submitted for admin review (6/6 sections complete)", tutorId);
            return true;
        }

        // ─── Private helpers ─────────────────────────────────────────────────

        private async Task ValidateSubjectGradePricesAsync(string tutorId, List<TutorSubjectGradePriceRequest> prices)
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

            // Rule 1 & 2: validate session duration and sessions/week against tutor availability
            var availabilities = await _unitOfWork.TutorRepository.GetAvailabilitiesByTutorIdAsync(tutorId);
            if (!availabilities.Any())
            {
                throw new ArgumentException("Vui lòng thiết lập lịch rảnh trước khi cấu hình môn/giá dạy.");
            }

            var blockMinutes = BuildContiguousBlocksInMinutes(availabilities);
            var maxBlockMinutes = blockMinutes.Max();
            var totalCapacityBase = blockMinutes; // reused per entry

            foreach (var p in prices)
            {
                // Rule 1: duration must be positive multiple of 30 min and fit in longest block
                if (p.DurationMinutesPerSession <= 0 || p.DurationMinutesPerSession % 30 != 0)
                {
                    throw new ArgumentException(
                        $"Số phút mỗi buổi (subjectId={p.SubjectId}, gradeId={p.GradeLevelId}) phải lớn hơn 0 và là bội số của 30.");
                }

                if (p.DurationMinutesPerSession > maxBlockMinutes)
                {
                    var maxHours = maxBlockMinutes / 60.0;
                    var maxHoursDisplay = maxHours % 1 == 0 ? $"{(int)maxHours}" : $"{maxHours:0.#}";
                    throw new ArgumentException(
                        $"Số giờ mỗi buổi vượt quá khoảng rảnh liên tục dài nhất ({maxHoursDisplay} giờ) theo lịch rảnh của bạn.");
                }

                // Rule 2: sessions/week must not exceed total slots available across all blocks
                var maxSessions = totalCapacityBase.Sum(b => b / p.DurationMinutesPerSession);
                if (p.SessionsPerWeek < 1)
                {
                    throw new ArgumentException(
                        $"Số buổi mỗi tuần (subjectId={p.SubjectId}, gradeId={p.GradeLevelId}) phải ít nhất là 1.");
                }

                if (p.SessionsPerWeek > maxSessions)
                {
                    throw new ArgumentException(
                        $"Số buổi mỗi tuần vượt quá số buổi tối đa ({maxSessions}) có thể xếp với lịch rảnh hiện tại.");
                }
            }
        }

        /// <summary>
        /// Groups availability slots (stored in UTC) by local day, merges contiguous 30-min slots,
        /// and returns the duration in minutes of each resulting contiguous block.
        /// </summary>
        private static List<int> BuildContiguousBlocksInMinutes(List<MV.DomainLayer.Entities.Tutoravailability> slots)
        {
            // Slots are stored in UTC — FE handles display conversion, group by UTC day
            var localSlots = slots
                .Where(s => s.Starttime.HasValue && s.Endtime.HasValue && s.Dayofweek.HasValue)
                .Select(s => (localDay: s.Dayofweek!.Value, localStart: s.Starttime!.Value, localEnd: s.Endtime!.Value))
                .GroupBy(s => s.localDay)
                .ToList();

            var blocks = new List<int>();

            foreach (var dayGroup in localSlots)
            {
                // Sort by start time within the day
                var sorted = dayGroup.OrderBy(s => s.localStart).ToList();

                var blockStart = sorted[0].localStart;
                var blockEnd = sorted[0].localEnd;

                for (int i = 1; i < sorted.Count; i++)
                {
                    if (sorted[i].localStart <= blockEnd)
                    {
                        // Contiguous or overlapping — extend the block
                        if (sorted[i].localEnd > blockEnd)
                            blockEnd = sorted[i].localEnd;
                    }
                    else
                    {
                        // Gap — close current block, start new one
                        blocks.Add((int)(blockEnd - blockStart).TotalMinutes);
                        blockStart = sorted[i].localStart;
                        blockEnd = sorted[i].localEnd;
                    }
                }

                blocks.Add((int)(blockEnd - blockStart).TotalMinutes);
            }

            return blocks;
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
                Durationminutespersession = p.DurationMinutesPerSession,
                Sessionsperweek = p.SessionsPerWeek,
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
                DurationMinutesPerSession = price.Durationminutespersession,
                SessionsPerWeek = price.Sessionsperweek,
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
                if (slot.DayOfWeek < 1 || slot.DayOfWeek > 7)
                {
                    throw new ArgumentException("DayOfWeek phải nằm trong khoảng 1-7 (1=Monday, 7=Sunday)");
                }

                if (!TimeOnly.TryParse(slot.StartTime, out var start) || !TimeOnly.TryParse(slot.EndTime, out var end))
                {
                    throw new ArgumentException("StartTime/EndTime phải đúng định dạng HH:mm");
                }

                if (start >= end)
                {
                    throw new ArgumentException("StartTime phải trước EndTime");
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
                IsActive = package.Isactive,
                FixedSlots = package.Tutorpackagefixedslots
                    .Select(s => new TutorPackageFixedSlotResponse
                    {
                        FixedSlotId = s.Fixedslotid,
                        DayOfWeek   = s.Dayofweek,
                        StartTime   = s.Starttime.ToString("HH:mm"),
                        EndTime     = s.Endtime.ToString("HH:mm")
                    })
                    .OrderBy(s => s.DayOfWeek)
                    .ThenBy(s => s.StartTime)
                    .ToList()
            };
        }
    }
}
