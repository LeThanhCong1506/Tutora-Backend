using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.RepositoryInterfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.Helpers;
using static MV.DomainLayer.Constants.ClassSessionStatus;

namespace MV.ApplicationLayer.BackgroundJobs;

/// <summary>
/// Background job for auto-confirming classSessions past their deadline
/// Runs every 5 minutes
/// </summary>
public class AutoConfirmClassSessionJob : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AutoConfirmClassSessionJob> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(5);

    public AutoConfirmClassSessionJob(IServiceProvider serviceProvider, ILogger<AutoConfirmClassSessionJob> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AutoConfirmClassSessionJob started");
        // Wait for app to fully start before first execution
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessUpcomingConfirmDeadlineRemindersAsync(stoppingToken);
                await ProcessAutoConfirmAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi trong AutoConfirmClassSessionJob.");
            }

            await Task.Delay(_interval, stoppingToken);
        }

        _logger.LogInformation("AutoConfirmClassSessionJob đã dừng.");
    }

    private async Task ProcessUpcomingConfirmDeadlineRemindersAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MV.ApplicationLayer.Interfaces.IAppDbContext>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var notificationRepo = scope.ServiceProvider.GetRequiredService<INotificationRepository>();
        var now = TimeZoneHelper.UtcNow;
        var deadlineWindow = now.AddHours(2);

        var classSessionsToRemind = await context.ClassSessions
            .Include(l => l.Booking)
                .ThenInclude(b => b!.Student)
            .Where(l => l.Status == PendingConfirmation &&
                        l.Confirmdeadline.HasValue &&
                        l.Confirmdeadline > now &&
                        l.Confirmdeadline <= deadlineWindow &&
                        l.Issettled != true)
            .ToListAsync(ct);

        foreach (var classSession in classSessionsToRemind)
        {
            var parentId = classSession.Booking?.Student?.Parentid ?? classSession.Booking?.Parentid;
            if (string.IsNullOrWhiteSpace(parentId))
                continue;

            var hasOpenDispute = await context.Disputes.AnyAsync(
                d => d.Classsessionid == classSession.Classsessionid &&
                     d.Status != DisputeStatus.Resolved &&
                     d.Status != DisputeStatus.Closed,
                ct);

            if (hasOpenDispute)
                continue;

            var alreadySent = await notificationRepo.ExistsByUserAndTypeAndReferenceAsync(
                parentId,
                NotificationType.LessonConfirmDeadline,
                classSession.Classsessionid.ToString());

            if (alreadySent)
                continue;

            try
            {
                await notificationService.CreateNotificationAsync(new NotificationRequest
                {
                    Userid = parentId,
                    Title = "Sắp hết hạn xác nhận buổi học",
                    Message = $"Con 2 giờ để xác nhận buổi học #{classSession.Classsessionid}, nếu không hệ thống sẽ tự xác nhận.",
                    Type = NotificationType.LessonConfirmDeadline,
                    Referenceid = classSession.Classsessionid.ToString()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Không thể gửi nhắc hạn xác nhận cho classSession {ClassSessionId}.", classSession.Classsessionid);
            }
        }
    }

    private async Task ProcessAutoConfirmAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var settlementService = scope.ServiceProvider.GetRequiredService<ISettlementService>();

        var confirmedCount = await settlementService.ProcessAutoConfirmAsync(ct);

        if (confirmedCount > 0)
        {
            _logger.LogInformation("Đã tự động xác nhận {Count} bài học.", confirmedCount);
        }
    }
}

/// <summary>
/// Background job for auto-unsuspending users
/// Runs every 30 minutes
/// </summary>
public class AutoUnsuspendJob : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AutoUnsuspendJob> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(30);

    public AutoUnsuspendJob(IServiceProvider serviceProvider, ILogger<AutoUnsuspendJob> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Công việc AutoUnsuspendJob đã bắt đầu.");
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessAutoUnsuspendAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi trong AutoUnsuspendJob.");
            }

            await Task.Delay(_interval, stoppingToken);
        }

        _logger.LogInformation("Công việc AutoUnsuspendJob đã dừng.");
    }

    private async Task ProcessAutoUnsuspendAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var warningService = scope.ServiceProvider.GetRequiredService<IWarningService>();

        var unsuspendedCount = await warningService.ProcessAutoUnsuspendAsync(ct);

        if (unsuspendedCount > 0)
        {
            _logger.LogInformation("Tự động bỏ tạm dừng {Count} người dùng.", unsuspendedCount);
        }
    }
}

/// <summary>
/// Background job for sending classSession reminders
/// Runs every 15 minutes
/// </summary>
public class ClassSessionReminderJob : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ClassSessionReminderJob> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(15);

    public ClassSessionReminderJob(IServiceProvider serviceProvider, ILogger<ClassSessionReminderJob> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Công việc nhắc nhở bài học đã bắt đầu.");
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SendRemindersAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi trong ClassSessionReminderJob.");
            }

            await Task.Delay(_interval, stoppingToken);
        }

        _logger.LogInformation("ClassSessionReminderJob dừng.");
    }

    private async Task SendRemindersAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MV.ApplicationLayer.Interfaces.IAppDbContext>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var zaloOAService = scope.ServiceProvider.GetRequiredService<IZaloOAService>();

        var now = TimeZoneHelper.UtcNow;
        var reminderWindow = now.AddMinutes(30);

        // Find classSessions starting within 30 minutes that haven't been reminded
        var upcomingClassSessions = await context.ClassSessions
            .Where(l => l.Status == Scheduled &&
                        l.Scheduledstart > now &&
                        l.Scheduledstart <= reminderWindow &&
                        l.Autoreportsent != true) // Reuse autoreportsent as reminder flag
            .Include(l => l.Booking)
                .ThenInclude(b => b!.Tutorsubjectgradeprice)
                    .ThenInclude(p => p!.Subject)
            .Include(l => l.Booking)
                .ThenInclude(b => b!.Student)
            .ToListAsync(ct);

        if (!upcomingClassSessions.Any())
        {
            _logger.LogDebug("ClassSessionReminderJob: Không có bài học nào sắp tới cần nhắc nhở.");
            return;
        }

        foreach (var classSession in upcomingClassSessions)
        {
            try
            {
                var subjectName = classSession.Booking?.Subject?.Subjectname ?? DisplayValues.NotAvailable;
                var minutesUntil = (int)(classSession.Scheduledstart - now).TotalMinutes;

                // Remind Tutor
                if (!string.IsNullOrEmpty(classSession.Tutorid))
                {
                    await notificationService.CreateNotificationAsync(new MV.DomainLayer.DTO.RequestModel.NotificationRequest
                    {
                        Userid = classSession.Tutorid,
                        Title = "Nhắc nhở buổi học",
                        Message = $"Buổi học môn {subjectName} sẽ bắt đầu trong {minutesUntil} phút. Hãy chuẩn bị sẵn sàng!",
                        Type = NotificationType.LessonReminder,
                        Referenceid = classSession.Classsessionid.ToString()
                    });
                }

                // Remind Parent
                var parentId = classSession.Booking?.Student?.Parentid;
                if (!string.IsNullOrEmpty(parentId))
                {
                    await notificationService.CreateNotificationAsync(new MV.DomainLayer.DTO.RequestModel.NotificationRequest
                    {
                        Userid = parentId,
                        Title = "Nhắc nhở buổi học",
                        Message = $"Buổi học môn {subjectName} của con bạn sẽ bắt đầu trong {minutesUntil} phút.",
                        Type = NotificationType.LessonReminder,
                        Referenceid = classSession.Classsessionid.ToString()
                    });

                    // Zalo ZNS reminder (only if user has Zalo linked + notifications enabled)
                    var vnTime = classSession.Scheduledstart.ToString("HH:mm dd/MM");
                    await zaloOAService.SendNotificationAsync(
                        parentId,
                        NotificationType.LessonReminder,
                        new Dictionary<string, string>
                        {
                            { "subject", subjectName },
                            { "time", vnTime },
                            { "minutes", minutesUntil.ToString() }
                        });
                }

                // Mark as reminded (reuse autoreportsent flag)
                classSession.Autoreportsent = true;
                classSession.Autoreportsentat = now;

                _logger.LogInformation("Đã gửi lời nhắc nhở về bài học {ClassSessionId} bắt đầu từ {Minutes} phút.", classSession.Classsessionid, minutesUntil);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi gửi lời nhắc nhở cho bài học {ClassSessionId}.", classSession.Classsessionid);
            }
        }

        await context.SaveChangesAsync(ct);
        _logger.LogInformation("ClassSessionReminderJob: Đã gửi lời nhắc cho {Count} bài học.", upcomingClassSessions.Count);
    }
}

/// <summary>
/// Background job that triggers remaining payment request after first classSession is confirmed (24h after report)
/// with no open disputes. Transitions booking from deposit_paid to pending_remaining_payment.
/// Runs every hour.
/// </summary>
public class RemainingPaymentTriggerJob : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RemainingPaymentTriggerJob> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromHours(1);

    public RemainingPaymentTriggerJob(IServiceProvider serviceProvider, ILogger<RemainingPaymentTriggerJob> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RemainingPaymentTriggerJob bắt đầu.");
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessRemainingPaymentTriggersAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi trong RemainingPaymentTriggerJob.");
            }

            await Task.Delay(_interval, stoppingToken);
        }

        _logger.LogInformation("RemainingPaymentTriggerJob dừng.");
    }

    private async Task ProcessRemainingPaymentTriggersAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MV.ApplicationLayer.Interfaces.IAppDbContext>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var now = TimeZoneHelper.UtcNow;

        // Find bookings that are deposit_paid, remaining not yet paid,
        // and have at least one classSession whose confirm deadline has passed (24h after report)
        // with no open disputes
        var bookingsToTrigger = await context.Bookings
            .Where(b => b.Status == BookingStatus.DepositPaid
                        && b.Remainingpaidat == null)
            .Include(b => b.ClassSessions)
            .Include(b => b.Student)
            .ToListAsync(ct);

        var triggeredCount = 0;

        foreach (var booking in bookingsToTrigger)
        {
            // Check if any classSession has passed its 24h confirmation deadline
            var confirmedClassSession = booking.ClassSessions.FirstOrDefault(
                l => l.Status == PendingConfirmation
                    && l.Confirmdeadline.HasValue
                    && l.Confirmdeadline.Value <= now);

            // Also check for already-completed classSessions
            var completedClassSession = booking.ClassSessions.FirstOrDefault(l => l.Status == Completed);

            if (confirmedClassSession == null && completedClassSession == null)
                continue;

            // Check no open disputes for this booking
            var hasOpenDispute = await context.Disputes
                .AnyAsync(d => d.Bookingid == booking.Bookingid
                    && d.Status != DisputeStatus.Resolved && d.Status != DisputeStatus.Closed, ct);

            if (hasOpenDispute)
            {
                _logger.LogInformation("Đặt chỗ {BookingId} có tranh chấp đang mở, bỏ qua bước kích hoạt thanh toán còn lại.", booking.Bookingid);
                continue;
            }

            // Transition to pending_remaining_payment
            booking.Status = BookingStatus.PendingRemainingPayment;
            booking.Paymentdueat = now.AddHours(48);
            booking.Updatedat = now;

            var parentId = booking.Student?.Parentid ?? booking.Parentid;
            try
            {
                await context.SaveChangesAsync(ct);
                triggeredCount++;
                _logger.LogInformation("Đã kích hoạt thanh toán phần còn lại cho đặt phòng {BookingId}", booking.Bookingid);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Không thể lưu thông báo thanh toán còn lại cho đặt chỗ {BookingId}.", booking.Bookingid);
                continue;
            }

            try
            {
                if (!string.IsNullOrEmpty(parentId))
                {
                    await notificationService.CreateNotificationAsync(new NotificationRequest
                    {
                        Userid = parentId,
                        Title = "Thanh toán các buổi học còn lại",
                        Message = $"Buổi học đầu tiên của booking #{booking.Bookingid} đã được xác nhận thành công. " +
                            $"Vui lòng thanh toán {booking.Remainingamount:N0}đ cho các buổi còn lại trong vòng 48h để tiếp tục học.",
                        Type = NotificationType.PaymentRemainingRequired,
                        Referenceid = booking.Bookingid.ToString()
                    });
                }

                if (!string.IsNullOrEmpty(booking.Tutorid))
                {
                    await notificationService.CreateNotificationAsync(new NotificationRequest
                    {
                        Userid = booking.Tutorid,
                        Title = "Đang chờ thanh toán phần còn lại",
                        Message = $"Hệ thống đã yêu cầu phụ huynh thanh toán các buổi học còn lại cho booking #{booking.Bookingid}.",
                        Type = NotificationType.PaymentRemainingRequired,
                        Referenceid = booking.Bookingid.ToString()
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Không thể gửi thông báo thanh toán còn lại cho booking {BookingId}.", booking.Bookingid);
            }
        }

        if (triggeredCount > 0)
        {
            _logger.LogInformation("RemainingPaymentTriggerJob: Đã kích hoạt {Count} đặt chỗ cho khoản thanh toán còn lại.", triggeredCount);
        }
    }
}

/// <summary>
/// Quét các đề xuất đổi lịch đang chờ đã quá hạn (24h kể từ lúc tạo, hoặc 2h trước giờ học gốc —
/// cái nào tới trước) và chuyển sang Expired, báo người đề xuất.
/// Runs every 10 minutes.
/// </summary>
public class ClassSessionRescheduleProposalExpiryJob : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ClassSessionRescheduleProposalExpiryJob> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(10);

    public ClassSessionRescheduleProposalExpiryJob(IServiceProvider serviceProvider, ILogger<ClassSessionRescheduleProposalExpiryJob> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ClassSessionRescheduleProposalExpiryJob đã bắt đầu.");
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var rescheduleProposalService = scope.ServiceProvider.GetRequiredService<IClassSessionRescheduleProposalService>();
                var expiredCount = await rescheduleProposalService.ExpireOverdueProposalsAsync(stoppingToken);
                if (expiredCount > 0)
                    _logger.LogInformation("ClassSessionRescheduleProposalExpiryJob: Đã hết hạn {Count} đề xuất đổi lịch.", expiredCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi trong ClassSessionRescheduleProposalExpiryJob.");
            }

            await Task.Delay(_interval, stoppingToken);
        }

        _logger.LogInformation("ClassSessionRescheduleProposalExpiryJob đã dừng.");
    }
}

/// <summary>
/// Tự động đóng phòng học trực tuyến (RTC) khi đã quá giờ kết thúc dự kiến +
/// <see cref="MV.ApplicationLayer.Services.ClassSessionService.LiveSessionAutoEndGraceMinutes"/> phút
/// mà gia sư chưa tự bấm "Kết thúc buổi học". Cả gia sư lẫn học viên/phụ huynh sẽ cùng bị đưa ra
/// khỏi phòng ở nhịp heartbeat kế tiếp (thay vì một bên rời trước, bên kia rời sau không đồng bộ).
/// Runs every 1 minute.
/// </summary>
public class AutoEndLiveSessionJob : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AutoEndLiveSessionJob> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(1);

    public AutoEndLiveSessionJob(IServiceProvider serviceProvider, ILogger<AutoEndLiveSessionJob> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AutoEndLiveSessionJob đã bắt đầu.");
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessAutoEndAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi trong AutoEndLiveSessionJob.");
            }

            await Task.Delay(_interval, stoppingToken);
        }

        _logger.LogInformation("AutoEndLiveSessionJob đã dừng.");
    }

    private async Task ProcessAutoEndAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var classSessionService = scope.ServiceProvider.GetRequiredService<IClassSessionService>();

        var closedCount = await classSessionService.AutoCloseExpiredLiveSessionsAsync(ct);

        if (closedCount > 0)
        {
            _logger.LogInformation("AutoEndLiveSessionJob: Đã tự động đóng {Count} phòng học quá giờ.", closedCount);
        }
    }
}
