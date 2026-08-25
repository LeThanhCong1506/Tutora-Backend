using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Helpers;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.RepositoryInterfaces;

namespace MV.ApplicationLayer.Services;

/// <summary>
/// Service for managing user warnings and suspensions
/// </summary>
public class WarningService : IWarningService
{
    private readonly IWarningRepository _warningRepo;
    private readonly IUserRepository _userRepo;
    private readonly IAppDbContext _context; // retained only for transaction management
    private readonly INotificationService _notificationService;
    private readonly ISuspensionRefundService _suspensionRefundService;
    private readonly ILogger<WarningService> _logger;

    private const int TempSuspensionDays = 7;

    public WarningService(
        IWarningRepository warningRepo,
        IUserRepository userRepo,
        IAppDbContext context,
        INotificationService notificationService,
        ISuspensionRefundService suspensionRefundService,
        ILogger<WarningService> logger)
    {
        _warningRepo = warningRepo;
        _userRepo = userRepo;
        _context = context;
        _notificationService = notificationService;
        _suspensionRefundService = suspensionRefundService;
        _logger = logger;
    }

    public async Task<WarningHistoryResponse> CreateWarningAsync(string userId, CreateWarningRequest request, string issuedBy)
    {
        var user = await _userRepo.GetUserByIdAsync(userId)
            ?? throw new ArgumentException("Không tìm thấy người dùng");

        var warning = new Userwarning
        {
            Userid = userId,
            Warninglevel = request.WarningLevel,
            Reason = request.Reason,
            Relatedbookingid = request.RelatedBookingId,
            Issuedby = issuedBy,
            Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
        };

        _warningRepo.AddWarning(warning);
        await _warningRepo.SaveChangesAsync();

        _logger.LogInformation("Created warning {WarningId} level {Level} for user {UserId} by {IssuedBy}",
            warning.Warningid, warning.Warninglevel, userId, issuedBy);

        await _notificationService.CreateNotificationAsync(new NotificationRequest
        {
            Userid = userId,
            Title = "Cảnh báo vi phạm",
            Message = $"Bạn đã nhận cảnh báo cấp {warning.Warninglevel}. Lý do: {warning.Reason}. Vui lòng chú ý tuân thủ quy định.",
            Type = NotificationType.Warning,
            Referenceid = warning.Warningid.ToString()
        });

        await CheckAndApplySuspensionAsync(userId);

        var issuer = await _userRepo.GetUserByIdAsync(issuedBy);
        return new WarningHistoryResponse
        {
            WarningId = warning.Warningid,
            WarningLevel = warning.Warninglevel,
            Reason = warning.Reason,
            IssuedByName = issuer?.Fullname,
            RelatedBookingId = warning.Relatedbookingid,
            CreatedAt = warning.Createdat
        };
    }

    public async Task<bool> CheckAndApplySuspensionAsync(string userId)
    {
        var now = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
        var thirtyDaysAgo = now.AddDays(-30);

        var isSuspended = await _warningRepo.HasActiveSuspensionAsync(userId);
        if (isSuspended) return false;

        var recentWarnings = await _warningRepo.GetRecentWarningsAsync(userId, thirtyDaysAgo);

        // Cao (level 3): 1 lần → suspend ngay lập tức
        var highCount = recentWarnings.Count(w => w.Warninglevel == WarningLevel.High);

        // Thấp + Trung bình (level 1 & 2): cần 3 lần mới suspend
        var lowMediumCount = recentWarnings.Count(w =>
            w.Warninglevel == WarningLevel.Low || w.Warninglevel == WarningLevel.Medium);

        var shouldSuspend = highCount >= WarningLevel.HighSuspendThreshold
                         || lowMediumCount >= WarningLevel.LowMediumSuspendThreshold;

        if (shouldSuspend)
        {
            var recentSuspensions = await _warningRepo.CountRecentSuspensionsAsync(userId, SuspensionType.Temporary, thirtyDaysAgo);

            // Nếu đã từng bị tạm suspend trong 30 ngày → khóa vĩnh viễn
            if (recentSuspensions >= 1)
                await CreateSuspensionAsync(
                    userId,
                    SuspensionType.Permanent,
                    $"Khóa vĩnh viễn: tái phạm sau khi đã bị đình chỉ tạm thời trong vòng 30 ngày "
                        + $"({DescribeWarnings(highCount, lowMediumCount)}).",
                    0,
                    null); // null = system action, không link FK vào users table
            else
                await CreateSuspensionAsync(
                    userId,
                    SuspensionType.Temporary,
                    $"Tự động đình chỉ {TempSuspensionDays} ngày do {DescribeWarnings(highCount, lowMediumCount)} trong 30 ngày.",
                    TempSuspensionDays,
                    null); // null = system action, không link FK vào users table

            return true;
        }

        return false;
    }

    /// <summary>
    /// The suspension reason is shown to the operator in the CMS and to the user in their
    /// notification, so it reads as a sentence rather than the counter dump it used to be
    /// ("Auto-suspended 7 days: High=6, Low/Med=1 warnings in 30 days").
    /// </summary>
    private static string DescribeWarnings(int highCount, int lowMediumCount)
    {
        var parts = new List<string>();
        if (highCount > 0) parts.Add($"{highCount} cảnh cáo mức Cao");
        if (lowMediumCount > 0) parts.Add($"{lowMediumCount} cảnh cáo mức Thấp/Trung bình");
        return parts.Count > 0 ? string.Join(" và ", parts) : "vi phạm quy định";
    }

    public async Task<UserWarningSummaryResponse> GetUserWarningsAsync(string userId)
    {
        var user = await _userRepo.GetUserByIdAsync(userId)
            ?? throw new ArgumentException("Không tìm thấy người dùng");

        var warnings = await _warningRepo.GetAllWarningsAsync(userId);
        var activeSuspension = await _warningRepo.GetActiveSuspensionAsync(userId);

        return new UserWarningSummaryResponse
        {
            UserId = userId,
            FullName = user.Fullname,
            TotalWarnings = warnings.Count,
            IsSuspended = activeSuspension != null,
            // Report the type that was actually recorded. Deriving it from Enddate relabelled every
            // CMS-issued suspension ("hidden_1_week"/"account_locked") as "temporary", so this
            // summary disagreed with the suspension list, which reads the column.
            SuspensionType = activeSuspension?.Suspensiontype,
            SuspensionEndDate = activeSuspension?.Enddate.HasValue == true ? activeSuspension.Enddate.Value : (DateTime?)null,
            Warnings = warnings.Select(w => new WarningHistoryResponse
            {
                WarningId = w.Warningid,
                WarningLevel = w.Warninglevel,
                Reason = w.Reason,
                IssuedByName = w.IssuedbyNavigation?.Fullname,
                RelatedBookingId = w.Relatedbookingid,
                CreatedAt = w.Createdat
            }).ToList()
        };
    }

    public async Task<SuspensionListResponse> CreateSuspensionAsync(string userId, string suspensionType, string reason, int durationDays, string createdBy)
    {
        var user = await _userRepo.GetUserByIdAsync(userId)
            ?? throw new ArgumentException("Không tìm thấy người dùng");

        // Reuse an ambient transaction if the caller already opened one (e.g. no-show action,
        // dispute resolution with "create warning" checked), otherwise own a fresh one.
        // Prevents "a transaction is already in progress" crashing the outer refund/settlement.
        var ownsTx = _context.Database.CurrentTransaction is null;
        await using var tx = ownsTx
            ? await _context.Database.BeginTransactionAsync()
            : null;
        try
        {
            var existingSuspensions = await _context.Profilesuspensions
                .Where(s => s.Userid == userId && s.Isactive == true)
                .ToListAsync();

            foreach (var existing in existingSuspensions)
                existing.Isactive = false;

            var now = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
            // The duration decides the end date; the type only names the *kind* of
            // restriction. Callers use two vocabularies — "temporary"/"permanent"
            // from auto-suspension, "hidden_1_week"/"account_locked" from the CMS —
            // so requiring an exact match on "temporary" silently stored every
            // CMS-issued suspension with no end date, making it permanent and
            // invisible to the auto-unsuspend job despite the admin picking a
            // duration. Only an explicitly permanent type is open-ended now.
            DateTime? endDate = suspensionType != SuspensionType.Permanent && durationDays > 0
                ? now.AddDays(durationDays)
                : null;

            var suspension = new Profilesuspension
            {
                Userid = userId,
                Suspensiontype = suspensionType,
                Reason = reason,
                Startdate = now,
                Enddate = endDate,
                Createdby = createdBy,
                Isactive = true
            };

            _warningRepo.AddSuspension(suspension);
            user.Status = 0;

            var tutorProfile = await _warningRepo.GetTutorProfileAsync(userId);
            if (tutorProfile != null) tutorProfile.Ispublic = false;

            await _warningRepo.SaveChangesAsync();

            // A suspended tutor cannot teach the sessions already on their calendar, and the money
            // for those sessions is sitting frozen in escrow. Unwind them in the same transaction as
            // the suspension itself: a course must never be left half-cancelled if this rolls back.
            // Only tutors hold escrow, so this is a no-op for a suspended parent/student.
            var refundImpact = await _suspensionRefundService.CascadeSuspensionAsync(
                userId, endDate, reason);

            await _warningRepo.SaveChangesAsync();
            if (tx is not null) await tx.CommitAsync();

            _logger.LogInformation("Applied {SuspensionType} suspension to user {UserId} until {EndDate}",
                suspensionType, userId, endDate?.ToString() ?? SuspensionType.Permanent);

            // Notification only when this method owns the transaction — when called from within
            // an ambient transaction (no-show action, dispute resolution), the outer caller hasn't
            // committed yet, so notifying here could announce a suspension that later rolls back.
            if (ownsTx)
            {
                var suspensionMessage = endDate.HasValue
                    ? $"Tài khoản của bạn đã bị tạm ẩn đến {FormatVietnamTime(endDate.Value)}. Lý do: {reason}"
                    : $"Tài khoản của bạn đã bị khóa vĩnh viễn. Lý do: {reason}";

                await _notificationService.CreateNotificationAsync(new NotificationRequest
                {
                    Userid = userId,
                    Title = endDate.HasValue ? "Tài khoản bị tạm ẩn" : "Tài khoản bị khóa",
                    Message = suspensionMessage,
                    Type = NotificationType.Warning,
                    Referenceid = suspension.Suspensionid.ToString()
                });

                // The cascade ran inside our transaction, so it deliberately held its refund
                // notifications back. The money is committed now — safe to announce.
                await _suspensionRefundService.NotifyImpactAsync(refundImpact);
            }

            var creatorName = createdBy == SystemActors.SystemUpper
                ? SystemActors.DisplayName
                : (await _userRepo.GetUserByIdAsync(createdBy))?.Fullname;
            return new SuspensionListResponse
            {
                SuspensionId = suspension.Suspensionid,
                UserId = userId,
                UserName = user.Fullname,
                UserEmail = user.Email,
                SuspensionType = suspensionType,
                Reason = reason,
                StartDate = now,
                EndDate = endDate,
                CreatedByName = creatorName,
                IsActive = true,
                RefundImpact = refundImpact
            };
        }
        catch
        {
            // tx is null whenever we joined an ambient transaction — rolling back unconditionally
            // threw a NullReferenceException that replaced the real failure in the caller's log.
            // The owner of that ambient transaction is the one that must roll it back.
            if (tx is not null) await tx.RollbackAsync();
            throw;
        }
    }

    /// <summary>Timestamps are stored in UTC; user-facing text must read in local time.</summary>
    private static string FormatVietnamTime(DateTime utc)
    {
        var vietnamTimeZone = TimeZoneHelper.GetTimeZoneInfo("Asia/Ho_Chi_Minh");
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), vietnamTimeZone);
        return local.ToString("HH:mm dd/MM/yyyy");
    }

    public async Task<bool> UnsuspendUserAsync(string userId, string adminId)
    {
        var suspension = await _context.Profilesuspensions
            .FirstOrDefaultAsync(s => s.Userid == userId && s.Isactive == true);

        if (suspension == null) return false;

        suspension.Isactive = false;
        suspension.Enddate = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

        var user = await _userRepo.GetUserByIdAsync(userId);
        var restoredAccess = RestoreAccessAfterSuspension(user, await _warningRepo.GetTutorProfileAsync(userId));

        await _warningRepo.SaveChangesAsync();

        _logger.LogInformation("Removed suspension from user {UserId} by {AdminId}", userId, adminId);

        await NotifySuspensionLiftedAsync(userId, restoredAccess, suspension.Suspensionid);
        return true;
    }

    /// <summary>
    /// Puts a user back online after their suspension ends, without undoing decisions that were
    /// never part of that suspension. Returns whether the account can actually sign in again.
    /// </summary>
    private static bool RestoreAccessAfterSuspension(User? user, Tutorprofile? tutorProfile)
    {
        // A separate admin block writes to the same Status column. Clearing Status here would
        // silently un-block an account an admin deliberately shut down, so a blocked user stays
        // blocked and only the suspension record is lifted.
        var stillBlocked = user?.Isdeactivated == true;
        if (user != null && !stillBlocked) user.Status = 1;

        // Only a profile that was approved belongs back in search. Republishing a draft, a
        // pending-approval, or a rejected profile would put an unvetted tutor in front of parents.
        if (tutorProfile != null
            && !stillBlocked
            && string.Equals(tutorProfile.Profilestatus, TutorProfileStatus.Active, StringComparison.OrdinalIgnoreCase))
            tutorProfile.Ispublic = true;

        return !stillBlocked;
    }

    private async Task NotifySuspensionLiftedAsync(string userId, bool restoredAccess, int suspensionId)
    {
        try
        {
            await _notificationService.CreateNotificationAsync(new NotificationRequest
            {
                Userid = userId,
                Title = restoredAccess ? "Tài khoản đã được mở lại" : "Đã gỡ đình chỉ",
                Message = restoredAccess
                    ? "Đình chỉ đã được gỡ. Bạn có thể đăng nhập và nhận lịch dạy trở lại."
                    : "Đình chỉ đã được gỡ, nhưng tài khoản của bạn vẫn đang bị khóa bởi quản trị viên. Vui lòng liên hệ hỗ trợ.",
                Type = NotificationType.Warning,
                Referenceid = suspensionId.ToString()
            });
        }
        catch (Exception ex)
        {
            // Losing the announcement must not leave the user suspended.
            _logger.LogWarning(ex, "Failed to send unsuspend notification to user {UserId}", userId);
        }
    }

    public async Task<PagedList<SuspensionListResponse>> GetActiveSuspensionsAsync(int page, int pageSize)
    {
        var (items, total) = await _warningRepo.GetActiveSuspensionsPagedAsync(page, pageSize);

        var dtos = items.Select(s => new SuspensionListResponse
        {
            SuspensionId = s.Suspensionid,
            UserId = s.Userid,
            UserName = s.User?.Fullname,
            UserEmail = s.User?.Email,
            SuspensionType = s.Suspensiontype,
            Reason = s.Reason,
            StartDate = s.Startdate,
            EndDate = s.Enddate,
                    CreatedByName = s.CreatedbyNavigation?.Fullname ?? SystemActors.DisplayName,
            IsActive = s.Isactive
        }).ToList();

        return new PagedList<SuspensionListResponse>(dtos, total, page, pageSize);
    }

    public async Task<List<SuspensionListResponse>> GetUserSuspensionsAsync(string userId)
    {
        var user = await _userRepo.GetUserByIdAsync(userId)
            ?? throw new ArgumentException("Không tìm thấy người dùng");

        var suspensions = await _warningRepo.GetUserSuspensionsAsync(userId);

        return suspensions.Select(s => new SuspensionListResponse
        {
            SuspensionId = s.Suspensionid,
            UserId = s.Userid,
            UserName = user.Fullname,
            UserEmail = user.Email,
            SuspensionType = s.Suspensiontype,
            Reason = s.Reason,
            StartDate = s.Startdate,
            EndDate = s.Enddate,
            // Auto-suspensions carry no admin in Createdby, so fall back to the system actor.
            CreatedByName = s.CreatedbyNavigation?.Fullname ?? SystemActors.DisplayName,
            IsActive = s.Isactive
        }).ToList();
    }

    public async Task<int> ProcessAutoUnsuspendAsync(CancellationToken ct = default)
    {
        var expiredSuspensions = await _warningRepo.GetExpiredActiveSuspensionsAsync(MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow);
        var count = 0;
        var lifted = new List<(string UserId, bool RestoredAccess, int SuspensionId)>();

        foreach (var suspension in expiredSuspensions)
        {
            try
            {
                suspension.Isactive = false;

                var tutorProfile = await _warningRepo.GetTutorProfileAsync(suspension.Userid!);
                var restoredAccess = RestoreAccessAfterSuspension(suspension.User, tutorProfile);

                lifted.Add((suspension.Userid!, restoredAccess, suspension.Suspensionid));
                count++;
                _logger.LogInformation("Auto-unsuspended user {UserId}", suspension.Userid);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to auto-unsuspend user {UserId}", suspension.Userid);
            }
        }

        if (count > 0)
        {
            await _warningRepo.SaveChangesAsync();
            _logger.LogInformation("Auto-unsuspended {Count} users", count);

            // Only after the lift is persisted — telling someone they are back before the write
            // lands would send them to a login that still rejects them.
            foreach (var (userId, restoredAccess, suspensionId) in lifted)
                await NotifySuspensionLiftedAsync(userId, restoredAccess, suspensionId);
        }

        return count;
    }
}
