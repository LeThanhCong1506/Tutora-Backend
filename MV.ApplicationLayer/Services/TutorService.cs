using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.Helpers;
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
        private readonly IEncryptionService _encryption;
        private readonly ITutorProfileUpdateStagingService _updateStaging;
        private readonly IAppDbContext _context;
        private readonly IEkycService _ekyc;
        private readonly ITutorEmbedQueue _embedQueue;

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
            ILogger<TutorService> logger,
            IEncryptionService encryption,
            ITutorProfileUpdateStagingService updateStaging,
            IAppDbContext context,
            IEkycService ekyc,
            ITutorEmbedQueue embedQueue)
        {
            _unitOfWork = unitOfWork;
            _storageService = storageService;
            _fptAiService = fptAiService;
            _notificationService = notificationService;
            _logger = logger;
            _encryption = encryption;
            _updateStaging = updateStaging;
            _context = context;
            _ekyc = ekyc;
            _embedQueue = embedQueue;
        }

        /// <summary>
        /// true nếu profile đã từng được duyệt và đang hiển thị công khai — mọi chỉnh sửa nội
        /// dung từ lúc này trở đi phải qua staging (Redis) chờ Admin duyệt lại, thay vì ghi
        /// thẳng vào Tutorprofile, để Marketplace tiếp tục hiển thị thông tin cũ cho đến khi
        /// được duyệt.
        /// </summary>
        private static bool RequiresApprovalForEdits(Tutorprofile profile) =>
            string.Equals(profile.Profilestatus, TutorProfileStatus.Active, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Báo cho mọi Admin biết có yêu cầu cập nhật hồ sơ đang chờ duyệt — không có "Admin phụ
        /// trách" riêng cho từng tutor nên gửi cho tất cả, giống NotifyAdminsOfDisputeMessageAsync
        /// (DisputeService) / tạo tranh chấp (ParentService). Chạy nền, không throw để không làm
        /// hỏng thao tác lưu hồ sơ chính nếu có lỗi khi gửi thông báo.
        /// </summary>
        /// <param name="tutorId">Tutor vừa nộp thay đổi.</param>
        /// <param name="sectionLabel">
        /// Tên mục vừa sửa (vd "Thông tin cơ bản", "Video giới thiệu") — hiện trực tiếp trong nội
        /// dung thông báo để Admin biết ngay vị trí cần xem, không phải đoán hay bấm vào mới rõ.
        /// </param>
        private async Task NotifyAdminsOfProfileUpdateAsync(string tutorId, string sectionLabel)
        {
            try
            {
                var reviewerIds = await PermissionRecipients.ResolveAsync(
                    _context, Permissions.TutorProfileUpdateView);
                if (reviewerIds.Count == 0) return;

                var tutorName = (await _unitOfWork.UserRepository.GetUserByIdAsync(tutorId))?.Fullname ?? tutorId;

                await _notificationService.CreateNotificationsAsync(reviewerIds.Select(reviewerId => new NotificationRequest
                {
                    Userid = reviewerId,
                    Title = "Yêu cầu cập nhật hồ sơ gia sư",
                    Message = $"Gia sư {tutorName} vừa sửa mục \"{sectionLabel}\", đang chờ duyệt.",
                    Type = NotificationType.TutorProfileUpdateRequest
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NotifyAdminsOfProfileUpdateAsync failed for tutor {TutorId}", tutorId);
            }
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
                Degree = tutorEntity.Degree,
                Education = tutorEntity.Education,
                Experience = tutorEntity.Experience,
                Gpa = tutorEntity.Gpa,
                GpaScale = tutorEntity.Gpascale,
                VideoIntroUrl = tutorEntity.Videointrourl,
                TeachingAreaCity = tutorEntity.Teachingareacity,
                TeachingAreaDistrict = tutorEntity.Teachingareadistrict,
                IsAcceptingBookings = tutorEntity.Isacceptingbookings
            };
        }

        // ─── Profile Updates ─────────────────────────────────────────────────

        public async Task<ProfileUpdateOutcome> UpdateTutorBasicInfoAsync(string userId, UpdateTutorBasicInfoRequest request)
        {
            var profile = await _unitOfWork.TutorRepository.GetTutorProfileByIdAsync(userId);
            if (profile == null) return ProfileUpdateOutcome.NotFound;

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

            if (RequiresApprovalForEdits(profile))
            {
                await _updateStaging.UpsertPendingUpdateAsync(userId, pending =>
                {
                    pending.Headline = request.Headline;
                    pending.TeachingAreaCity = request.TeachingAreaCity;
                    pending.TeachingAreaDistrict = request.TeachingAreaDistrict;
                });
                await NotifyAdminsOfProfileUpdateAsync(userId, "Thông tin cơ bản");
                return ProfileUpdateOutcome.PendingApproval;
            }

            profile.Headline = request.Headline;
            profile.Teachingareacity = request.TeachingAreaCity;
            profile.Teachingareadistrict = request.TeachingAreaDistrict;
            profile.Updatedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

            await _unitOfWork.SaveChangesAsync();
            await AutoSubmitIfCompleteAsync(userId);
            return ProfileUpdateOutcome.Applied;
        }

        public async Task<ProfileUpdateOutcome> UpdateTutorIntroductionAsync(string userId, UpdateTutorIntroductionRequest request)
        {
            var profile = await _unitOfWork.TutorRepository.GetTutorProfileByIdAsync(userId);
            if (profile == null) return ProfileUpdateOutcome.NotFound;

            // GPA là tùy chọn, nhưng đã nhập điểm thì phải có thang điểm — nếu không, con số
            // "8.5" nằm trơ một mình trên hồ sơ không nói lên điều gì (thang 10 thì khá, thang
            // 4 thì vô nghĩa).
            if (request.Gpa.HasValue && !request.GpaScale.HasValue)
            {
                throw new ArgumentException("Vui lòng chọn thang điểm cho GPA.");
            }

            if (request.Gpa.HasValue && request.GpaScale.HasValue && request.Gpa > request.GpaScale)
            {
                throw new ArgumentException($"Điểm GPA ({request.Gpa}) không được vượt quá thang điểm ({request.GpaScale}).");
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

            if (RequiresApprovalForEdits(profile))
            {
                await _updateStaging.UpsertPendingUpdateAsync(userId, pending =>
                {
                    pending.Bio = request.Bio;
                    pending.Degree = request.Degree;
                    pending.Education = request.Education;
                    pending.GpaScale = request.GpaScale;
                    pending.Gpa = request.Gpa;
                    pending.Experience = request.Experience;
                });
                await NotifyAdminsOfProfileUpdateAsync(userId, "Giới thiệu bản thân");
                return ProfileUpdateOutcome.PendingApproval;
            }

            profile.Bio = request.Bio;
            profile.Degree = request.Degree;
            profile.Education = request.Education;
            profile.Gpascale = request.GpaScale;
            profile.Gpa = request.Gpa;
            profile.Experience = request.Experience;
            profile.Updatedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

            await _unitOfWork.SaveChangesAsync();
            await AutoSubmitIfCompleteAsync(userId);
            _embedQueue.Enqueue(userId);   // bio/education/experience đổi → re-embed nền
            return ProfileUpdateOutcome.Applied;
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
            await AutoSubmitIfCompleteAsync(userId);
            _embedQueue.Enqueue(userId);   // môn/giá đổi → refresh metadata vector nền
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

        public async Task<ProfileUpdateOutcome> UpdateTutorPricingAsync(string tutorId, UpdateTutorPricingRequest request)
        {
            var profile = await _unitOfWork.TutorRepository.GetTutorProfileByIdAsync(tutorId);
            if (profile == null) return ProfileUpdateOutcome.NotFound;

            if (!request.SubjectGradePrices.Any())
            {
                throw new ArgumentException("Cần ít nhất một giá theo môn và lớp");
            }

            await ValidateSubjectGradePricesAsync(tutorId, request.SubjectGradePrices);

            if (RequiresApprovalForEdits(profile))
            {
                await _updateStaging.UpsertPendingUpdateAsync(tutorId, pending =>
                {
                    pending.SubjectGradePrices = request.SubjectGradePrices;
                });
                await NotifyAdminsOfProfileUpdateAsync(tutorId, "Môn học & Bảng giá");
                return ProfileUpdateOutcome.PendingApproval;
            }

            profile.Updatedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

            await _unitOfWork.TutorRepository.ReplaceTutorSubjectGradePricesAsync(
                tutorId,
                MapSubjectGradePriceRequests(tutorId, request.SubjectGradePrices));

            await _unitOfWork.SaveChangesAsync();
            await AutoSubmitIfCompleteAsync(tutorId);
            _embedQueue.Enqueue(tutorId);   // giá đổi → refresh metadata vector nền
            return ProfileUpdateOutcome.Applied;
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

            await AutoSubmitIfCompleteAsync(tutorId);
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

            // Gắn cờ HasActiveBooking cho từng package (1 truy vấn dùng chung guard).
            var bookedPackageIds = await TutorScheduleGuard.GetPackageIdsWithFutureSessionsAsync(_context, tutorId);

            return packages
                .Select(p => MapTutorPackageResponse(p, bookedPackageIds.Contains(p.Packageid)))
                .ToList();
        }

        public async Task<TutorPackageResponse?> CreateTutorPackageAsync(string tutorId, CreateTutorPackageRequest request)
        {
            var profile = await _unitOfWork.TutorRepository.GetTutorProfileByIdAsync(tutorId);
            if (profile == null) return null;

            ValidateTutorPackageRequest(request);
            await ValidateTutorCanUsePackageSubjectAsync(tutorId, request);

            // Guard: không cho tạo khung cố định đè lên buổi dạy đã cam kết (tránh trùng lịch).
            var committed = await TutorScheduleGuard.GetFutureCommittedSessionsAsync(_context, tutorId);
            foreach (var s in request.FixedSlots)
            {
                var slotStart = TimeOnly.Parse(s.StartTime).ToTimeSpan();
                var slotEnd = TimeOnly.Parse(s.EndTime).ToTimeSpan();
                if (TutorScheduleGuard.OverlapsWeeklySlot(committed, s.DayOfWeek, slotStart, slotEnd))
                    throw new InvalidOperationException(
                        $"Không thể tạo khung {s.StartTime}-{s.EndTime} (thứ {s.DayOfWeek}) vì đã có buổi dạy được đặt ở khung giờ này.");
            }

            var now = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
            var package = new Tutorpackage
            {
                Tutorid = tutorId,
                Subjectid = request.SubjectId,
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

            // Guard: không cho tắt gói khi còn buổi dạy được đặt & chưa hoàn tất thuộc gói này.
            var committed = await TutorScheduleGuard.GetFutureCommittedSessionsAsync(_context, tutorId, packageId);
            if (committed.Count > 0)
                throw new InvalidOperationException(
                    "Không thể tắt gói này vì đang có buổi dạy được đặt và chưa hoàn tất. Vui lòng chờ hoàn tất hoặc hủy booking trước.");

            package.Isactive = false;
            package.Updatedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
            await _unitOfWork.SaveChangesAsync();
            return true;
        }

        /// <summary>
        /// Bật lại (hiện lại trên marketplace) một gói đã bị tắt trước đó.
        /// </summary>
        public async Task<bool> ActivateTutorPackageAsync(string tutorId, int packageId)
        {
            var package = await _unitOfWork.TutorRepository.GetTutorPackageAsync(tutorId, packageId);
            if (package == null) return false;

            package.Isactive = true;
            package.Updatedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
            await _unitOfWork.SaveChangesAsync();
            return true;
        }

        /// <summary>
        /// Xóa vĩnh viễn một package đã ẩn. Chỉ cho phép khi package đang Isactive=false
        /// (phải ẩn trước) và chưa từng có booking nào tham chiếu (booking giữ Packageid FK,
        /// không cascade delete — xóa cứng package còn booking sẽ vỡ ràng buộc dữ liệu).
        /// </summary>
        public async Task<bool> DeleteTutorPackageAsync(string tutorId, int packageId)
        {
            var package = await _unitOfWork.TutorRepository.GetTutorPackageAsync(tutorId, packageId);
            if (package == null) return false;

            if (package.Isactive)
                throw new InvalidOperationException("Vui lòng ẩn gói này trước khi xóa vĩnh viễn.");

            var hasBookings = await _unitOfWork.TutorRepository.TutorPackageHasBookingsAsync(packageId);
            if (hasBookings)
                throw new InvalidOperationException(
                    "Không thể xóa vĩnh viễn gói này vì đã từng có buổi dạy được đặt theo gói. Bạn có thể tiếp tục ẩn gói này.");

            _unitOfWork.TutorRepository.DeleteTutorPackage(package);
            await _unitOfWork.SaveChangesAsync();
            return true;
        }

        /// <summary>
        /// Cập nhật package (tên, loại, khung cố định). Trả null nếu không tìm thấy.
        /// Chặn (409) nếu package đang có buổi dạy được đặt chưa hoàn tất, hoặc khung mới
        /// đè lên buổi dạy đã cam kết khác.
        /// </summary>
        public async Task<TutorPackageResponse?> UpdateTutorPackageAsync(string tutorId, int packageId, CreateTutorPackageRequest request)
        {
            var package = await _unitOfWork.TutorRepository.GetTutorPackageAsync(tutorId, packageId);
            if (package == null) return null;

            ValidateTutorPackageRequest(request);
            await ValidateTutorCanUsePackageSubjectAsync(tutorId, request);

            // Guard 1: gói này còn buổi dạy chưa hoàn tất → khóa, không cho sửa.
            var ownCommitted = await TutorScheduleGuard.GetFutureCommittedSessionsAsync(_context, tutorId, packageId);
            if (ownCommitted.Count > 0)
                throw new InvalidOperationException(
                    "Không thể sửa gói này vì đang có buổi dạy được đặt và chưa hoàn tất. Vui lòng chờ hoàn tất hoặc hủy booking trước.");

            // Guard 2: khung cố định mới không được đè lên buổi dạy đã cam kết (gói khác / lịch khác).
            var allCommitted = await TutorScheduleGuard.GetFutureCommittedSessionsAsync(_context, tutorId);
            foreach (var s in request.FixedSlots)
            {
                var slotStart = TimeOnly.Parse(s.StartTime).ToTimeSpan();
                var slotEnd = TimeOnly.Parse(s.EndTime).ToTimeSpan();
                if (TutorScheduleGuard.OverlapsWeeklySlot(allCommitted, s.DayOfWeek, slotStart, slotEnd))
                    throw new InvalidOperationException(
                        $"Không thể đặt khung {s.StartTime}-{s.EndTime} (thứ {s.DayOfWeek}) vì đã có buổi dạy được đặt ở khung giờ này.");
            }

            var now = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
            package.Name = request.Name.Trim();
            package.Subjectid = request.SubjectId;
            package.Packagetype = request.PackageType;
            package.Updatedat = now;

            // Thay toàn bộ khung cố định (orphan cũ sẽ bị EF xóa do quan hệ bắt buộc).
            package.Tutorpackagefixedslots.Clear();
            foreach (var s in request.FixedSlots)
            {
                package.Tutorpackagefixedslots.Add(new Tutorpackagefixedslot
                {
                    Dayofweek = s.DayOfWeek,
                    Starttime = TimeOnly.Parse(s.StartTime),
                    Endtime   = TimeOnly.Parse(s.EndTime),
                    Createdat = now
                });
            }

            await _unitOfWork.SaveChangesAsync();

            // Qua được Guard 1 nghĩa là gói không còn booking → HasActiveBooking = false.
            return MapTutorPackageResponse(package, hasActiveBooking: false);
        }

        // ─── Status Management ────────────────────────────────────────────────

        /// <summary>
        /// Trả về trạng thái hoàn thành 6 mục hồ sơ gia sư.
        /// FE dùng để hiển thị progress bar và bật/tắt nút Submit.
        /// </summary>
        public async Task<ProfileCompletionResponse> GetProfileCompletionAsync(string tutorId)
        {
            var profile        = await _unitOfWork.TutorRepository.GetTutorProfileByIdAsync(tutorId);
            var user           = await _unitOfWork.UserRepository.GetUserByIdAsync(tutorId);
            var subjects       = await _unitOfWork.TutorRepository.GetTutorSubjectsByTutorIdAsync(tutorId);
            var prices         = await _unitOfWork.TutorRepository.GetTutorSubjectGradePricesAsync(tutorId);
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

            var sections = new List<ProfileSection>
            {
                new() { Key = "basic_info",    Name = "Thông tin cơ bản",     IsComplete = hasBasicInfo,
                    MissingReason = hasBasicInfo    ? null : "Cần điền Tiêu đề và Khu vực dạy học" },
                new() { Key = "introduction",  Name = "Giới thiệu bản thân",   IsComplete = hasIntroduction,
                    MissingReason = hasIntroduction ? null : "Cần điền Bio và Học vấn" },
                new() { Key = "subjects",      Name = "Môn học & Bảng giá",    IsComplete = hasSubjects,
                    MissingReason = hasSubjects     ? null : "Cần chọn ít nhất 1 môn học và thiết lập giá" },
                new() { Key = "availability",  Name = "Lịch rảnh",             IsComplete = hasAvailability,
                    MissingReason = hasAvailability ? null : "Cần thiết lập ít nhất 1 khung giờ rảnh" },
                new() { Key = "identity",      Name = "Xác minh CCCD",         IsComplete = hasIdentity,
                    MissingReason = hasIdentity     ? null : "Cần upload CCCD và chờ xác minh" },
            };

            int completedCount = sections.Count(s => s.IsComplete);
            bool canSubmit = completedCount == 5
                             && profile != null
                             && (string.Equals(profile.Profilestatus, TutorProfileStatus.Draft,    StringComparison.OrdinalIgnoreCase)
                              || string.Equals(profile.Profilestatus, TutorProfileStatus.Rejected, StringComparison.OrdinalIgnoreCase));

            var reportedStatus = profile?.Profilestatus ?? TutorProfileStatus.Draft;
            if (profile != null && RequiresApprovalForEdits(profile))
            {
                var pendingUpdate = await _updateStaging.GetPendingUpdateAsync(tutorId);
                if (pendingUpdate != null)
                {
                    reportedStatus = TutorProfileStatus.PendingUpdate;
                }
            }

            return new ProfileCompletionResponse
            {
                CompletedSections = completedCount,
                TotalSections     = 5,
                CanSubmit         = canSubmit,
                ProfileStatus     = reportedStatus,
                Sections          = sections
            };
        }

        /// <summary>
        /// Public entry point cho TutorAvailabilityService (service khác) gọi sau khi lưu availability.
        /// </summary>
        public Task TryAutoSubmitAsync(string tutorId) => AutoSubmitIfCompleteAsync(tutorId);

        /// <summary>
        /// Nếu đủ 6/6 mục VÀ status đang Draft hoặc Rejected → tự động chuyển sang PendingApproval.
        /// Được gọi sau mỗi lần Tutor cập nhật một trong 6 mục. Silent — không throw exception.
        /// </summary>
        public async Task<bool> SetAcceptingBookingsAsync(string userId, bool accepting)
        {
            var profile = await _unitOfWork.TutorRepository.GetTutorProfileByIdAsync(userId);
            if (profile == null) return false;
            profile.Isacceptingbookings = accepting;
            profile.Updatedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
            await _unitOfWork.SaveChangesAsync();
            return true;
        }

        private async Task AutoSubmitIfCompleteAsync(string tutorId)
        {
            try
            {
                var completion = await GetProfileCompletionAsync(tutorId);
                if (!completion.CanSubmit) return;

                var profile = await _unitOfWork.TutorRepository.GetTutorProfileByIdAsync(tutorId);
                if (profile == null) return;

                profile.Profilestatus = TutorProfileStatus.PendingApproval;
                profile.Updatedat     = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
                await _unitOfWork.SaveChangesAsync();

                _logger.LogInformation("Profile {TutorId} auto-submitted for admin review (6/6 complete)", tutorId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AutoSubmitIfCompleteAsync failed for tutor {TutorId}", tutorId);
            }
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
                throw new ArgumentException($"Subject IDs không tồn tại hoặc đã ngừng sử dụng: {string.Join(", ", invalidSubjectIds)}");
            }

            var gradeLevelIds = prices.Select(p => p.GradeLevelId).Distinct().ToList();
            var existingGradeLevelIds = await _unitOfWork.TutorRepository.GetExistingGradeLevelIdsAsync(gradeLevelIds);
            var invalidGradeLevelIds = gradeLevelIds.Except(existingGradeLevelIds).ToList();
            if (invalidGradeLevelIds.Any())
            {
                throw new ArgumentException($"GradeLevel IDs không tồn tại hoặc đã ngừng sử dụng: {string.Join(", ", invalidGradeLevelIds)}");
            }

            // Rule 0: khối lớp phải thuộc chương trình môn học (vd Vật Lý không áp dụng cho Lớp 1).
            // So theo Levelorder, không so trực tiếp id vì id không đảm bảo tăng dần theo khối lớp.
            var subjectsWithRange = await _unitOfWork.TutorRepository.GetSubjectsWithGradeRangeAsync(subjectIds);
            var subjectById = subjectsWithRange.ToDictionary(s => s.Subjectid);
            var gradeLevelOrders = await _unitOfWork.TutorRepository.GetGradeLevelOrdersAsync(gradeLevelIds);

            foreach (var p in prices)
            {
                var subject = subjectById[p.SubjectId];
                if (subject.MinGradeLevel == null && subject.MaxGradeLevel == null) continue;

                var targetOrder = gradeLevelOrders[p.GradeLevelId];
                var minOrder = subject.MinGradeLevel?.Levelorder ?? int.MinValue;
                var maxOrder = subject.MaxGradeLevel?.Levelorder ?? int.MaxValue;
                if (targetOrder < minOrder || targetOrder > maxOrder)
                {
                    var rangeText = subject.MinGradeLevel != null && subject.MaxGradeLevel != null
                        ? $"từ {subject.MinGradeLevel.Gradename} đến {subject.MaxGradeLevel.Gradename}"
                        : subject.MinGradeLevel != null
                            ? $"từ {subject.MinGradeLevel.Gradename} trở lên"
                            : $"đến {subject.MaxGradeLevel!.Gradename}";
                    throw new ArgumentException(
                        $"Khối lớp không thuộc chương trình môn {subject.Subjectname} (áp dụng {rangeText}).");
                }
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
                IsActive = price.Isactive,
                // Cờ để FE biết môn/khối đã bị Admin soft-delete: cảnh báo/khóa dòng này.
                // Nav property được load sẵn qua Include; mặc định true nếu chưa load được.
                SubjectIsActive = price.Subject?.IsActive ?? true,
                GradeLevelIsActive = price.Gradelevel?.IsActive ?? true
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

            if (request.PackageType == Tutorpackage.FixedPackageType && request.SubjectId is not > 0)
            {
                throw new ArgumentException("Package fixed phải chọn môn học");
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

            // Không cho phép các khung giờ trong cùng một ngày đè/trùng lên nhau.
            // Mỗi khung thời gian chỉ được có duy nhất một lịch dạy — ví dụ 20:00-22:00 và
            // 21:00-22:00 (thứ 2) là chồng lấn nên phải báo lỗi.
            var slotsByDay = request.FixedSlots
                .Select(s => new
                {
                    s.DayOfWeek,
                    Start = TimeOnly.Parse(s.StartTime),
                    End = TimeOnly.Parse(s.EndTime)
                })
                .GroupBy(s => s.DayOfWeek);

            foreach (var day in slotsByDay)
            {
                var ordered = day.OrderBy(s => s.Start).ToList();
                var maxEnd = ordered[0].End;
                for (int i = 1; i < ordered.Count; i++)
                {
                    // Đã sắp theo Start tăng dần: nếu Start của khung sau < End lớn nhất
                    // của các khung trước đó thì hai khung chồng lấn thời gian.
                    if (ordered[i].Start < maxEnd)
                    {
                        throw new ArgumentException(
                            $"Các khung giờ trong cùng một ngày (thứ {day.Key}) không được trùng/đè lên nhau. " +
                            "Mỗi khung thời gian chỉ được xếp một lịch dạy.");
                    }
                    if (ordered[i].End > maxEnd) maxEnd = ordered[i].End;
                }
            }
        }

        private async Task ValidateTutorCanUsePackageSubjectAsync(string tutorId, CreateTutorPackageRequest request)
        {
            if (request.SubjectId is not > 0) return;

            var isValidSubject = await _context.Subjects.AnyAsync(subject =>
                subject.Subjectid == request.SubjectId && subject.IsActive);
            if (!isValidSubject)
                throw new ArgumentException("Môn học không tồn tại hoặc đã ngừng cung cấp");

            var tutorTeachesSubject = await _context.Tutorsubjectgradeprices.AnyAsync(price =>
                price.Tutorid == tutorId &&
                price.Subjectid == request.SubjectId &&
                price.Isactive);
            if (!tutorTeachesSubject)
                throw new ArgumentException("Gia sư chưa cấu hình giá cho môn học này");
        }

        private static TutorPackageResponse MapTutorPackageResponse(Tutorpackage package, bool hasActiveBooking = false)
        {
            return new TutorPackageResponse
            {
                PackageId = package.Packageid,
                TutorId = package.Tutorid,
                SubjectId = package.Subjectid,
                SubjectName = package.Subject?.Subjectname,
                Name = package.Name,
                PackageType = package.Packagetype,
                IsActive = package.Isactive,
                HasActiveBooking = hasActiveBooking,
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
