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
    private readonly ILogger<WarningService> _logger;

    private const int TempSuspensionDays = 7;

    public WarningService(
        IWarningRepository warningRepo,
        IUserRepository userRepo,
        IAppDbContext context,
        INotificationService notificationService,
        ILogger<WarningService> logger)
    {
        _warningRepo = warningRepo;
        _userRepo = userRepo;
        _context = context;
        _notificationService = notificationService;
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
            Message = $"Bạn đã nhận cảnh báo cấp {warning.Warninglevel}. Lý do: {warning.Reason}. Vui lòng chú ý tuân thủ quy định."
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
            CreatedAt = warning.Createdat.HasValue ? TimeZoneHelper.ToUserTime(warning.Createdat.Value) : (DateTime?)null
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
                    $"Auto-suspended permanently: Excessive violations in 30 days (High={highCount}, Low/Med={lowMediumCount})",
                    0,
                    null); // null = system action, không link FK vào users table
            else
                await CreateSuspensionAsync(
                    userId,
                    SuspensionType.Temporary,
                    $"Auto-suspended {TempSuspensionDays} days: High={highCount}, Low/Med={lowMediumCount} warnings in 30 days",
                    TempSuspensionDays,
                    null); // null = system action, không link FK vào users table

            return true;
        }

        return false;
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
            SuspensionType = activeSuspension != null
                ? (activeSuspension.Enddate.HasValue ? SuspensionType.Temporary : SuspensionType.Permanent)
                : null,
            SuspensionEndDate = activeSuspension?.Enddate.HasValue == true ? TimeZoneHelper.ToUserTime(activeSuspension.Enddate.Value) : (DateTime?)null,
            Warnings = warnings.Select(w => new WarningHistoryResponse
            {
                WarningId = w.Warningid,
                WarningLevel = w.Warninglevel,
                Reason = w.Reason,
                IssuedByName = w.IssuedbyNavigation?.Fullname,
                RelatedBookingId = w.Relatedbookingid,
                CreatedAt = w.Createdat.HasValue ? TimeZoneHelper.ToUserTime(w.Createdat.Value) : (DateTime?)null
            }).ToList()
        };
    }

    public async Task<SuspensionListResponse> CreateSuspensionAsync(string userId, string suspensionType, string reason, int durationDays, string createdBy)
    {
        var user = await _userRepo.GetUserByIdAsync(userId)
            ?? throw new ArgumentException("Không tìm thấy người dùng");

        await using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            var existingSuspensions = await _context.Profilesuspensions
                .Where(s => s.Userid == userId && s.Isactive == true)
                .ToListAsync();

            foreach (var existing in existingSuspensions)
                existing.Isactive = false;

            var now = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
            DateTime? endDate = suspensionType == SuspensionType.Temporary && durationDays > 0
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
            await tx.CommitAsync();

            _logger.LogInformation("Applied {SuspensionType} suspension to user {UserId} until {EndDate}",
                suspensionType, userId, endDate?.ToString() ?? SuspensionType.Permanent);

            var suspensionMessage = suspensionType == SuspensionType.Temporary
                ? $"Tài khoản của bạn đã bị tạm ẩn đến {endDate:dd/MM/yyyy HH:mm}. Lý do: {reason}"
                : $"Tài khoản của bạn đã bị khóa vĩnh viễn. Lý do: {reason}";

            await _notificationService.CreateNotificationAsync(new NotificationRequest
            {
                Userid = userId,
                Title = suspensionType == SuspensionType.Temporary ? "Tài khoản bị tạm ẩn" : "Tài khoản bị khóa",
                Message = suspensionMessage
            });

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
                StartDate = TimeZoneHelper.ToUserTime(now),
                EndDate = endDate.HasValue ? TimeZoneHelper.ToUserTime(endDate.Value) : (DateTime?)null,
                CreatedByName = creatorName,
                IsActive = true
            };
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task<bool> UnsuspendUserAsync(string userId, string adminId)
    {
        var suspension = await _context.Profilesuspensions
            .FirstOrDefaultAsync(s => s.Userid == userId && s.Isactive == true);

        if (suspension == null) return false;

        suspension.Isactive = false;
        suspension.Enddate = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

        var user = await _userRepo.GetUserByIdAsync(userId);
        if (user != null) user.Status = 1;

        var tutorProfile = await _warningRepo.GetTutorProfileAsync(userId);
        if (tutorProfile != null) tutorProfile.Ispublic = true;

        await _warningRepo.SaveChangesAsync();

        _logger.LogInformation("Removed suspension from user {UserId} by {AdminId}", userId, adminId);
        return true;
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
            StartDate = s.Startdate.HasValue ? TimeZoneHelper.ToUserTime(s.Startdate.Value) : (DateTime?)null,
            EndDate = s.Enddate.HasValue ? TimeZoneHelper.ToUserTime(s.Enddate.Value) : (DateTime?)null,
                    CreatedByName = s.CreatedbyNavigation?.Fullname ?? SystemActors.DisplayName,
            IsActive = s.Isactive
        }).ToList();

        return new PagedList<SuspensionListResponse>(dtos, total, page, pageSize);
    }

    public async Task<int> ProcessAutoUnsuspendAsync(CancellationToken ct = default)
    {
        var expiredSuspensions = await _warningRepo.GetExpiredActiveSuspensionsAsync(MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow);
        var count = 0;

        foreach (var suspension in expiredSuspensions)
        {
            try
            {
                suspension.Isactive = false;
                if (suspension.User != null) suspension.User.Status = 1;

                var tutorProfile = await _warningRepo.GetTutorProfileAsync(suspension.Userid!);
                if (tutorProfile != null) tutorProfile.Ispublic = true;

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
        }

        return count;
    }
}
