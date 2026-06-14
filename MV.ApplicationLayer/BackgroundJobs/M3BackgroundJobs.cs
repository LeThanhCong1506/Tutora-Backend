using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.RepositoryInterfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.Helpers;
using static MV.DomainLayer.Constants.LessonStatus;

namespace MV.ApplicationLayer.BackgroundJobs;

/// <summary>
/// Background job for auto-confirming lessons past their deadline
/// Runs every 5 minutes
/// </summary>
public class AutoConfirmLessonJob : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AutoConfirmLessonJob> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(5);

    public AutoConfirmLessonJob(IServiceProvider serviceProvider, ILogger<AutoConfirmLessonJob> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AutoConfirmLessonJob started");
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
                _logger.LogError(ex, "Lỗi trong AutoConfirmLessonJob.");
            }

            await Task.Delay(_interval, stoppingToken);
        }

        _logger.LogInformation("AutoConfirmLessonJob đã dừng.");
    }

    private async Task ProcessUpcomingConfirmDeadlineRemindersAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MV.ApplicationLayer.Interfaces.IAppDbContext>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var notificationRepo = scope.ServiceProvider.GetRequiredService<INotificationRepository>();
        var now = TimeZoneHelper.UtcNow;
        var deadlineWindow = now.AddHours(2);

        var lessonsToRemind = await context.Lessons
            .Include(l => l.Booking)
                .ThenInclude(b => b!.Student)
            .Where(l => l.Status == PendingConfirmation &&
                        l.Confirmdeadline.HasValue &&
                        l.Confirmdeadline > now &&
                        l.Confirmdeadline <= deadlineWindow &&
                        l.Issettled != true)
            .ToListAsync(ct);

        foreach (var lesson in lessonsToRemind)
        {
            var parentId = lesson.Booking?.Student?.Parentid ?? lesson.Booking?.Parentid;
            if (string.IsNullOrWhiteSpace(parentId))
                continue;

            var hasOpenDispute = await context.Disputes.AnyAsync(
                d => d.Lessonid == lesson.Lessonid &&
                     d.Status != DisputeStatus.Resolved &&
                     d.Status != DisputeStatus.Closed,
                ct);

            if (hasOpenDispute)
                continue;

            var alreadySent = await notificationRepo.ExistsByUserAndTypeAndReferenceAsync(
                parentId,
                NotificationType.LessonConfirmDeadline,
                lesson.Lessonid.ToString());

            if (alreadySent)
                continue;

            try
            {
                await notificationService.CreateNotificationAsync(new NotificationRequest
                {
                    Userid = parentId,
                    Title = "Sắp hết hạn xác nhận buổi học",
                    Message = $"Con 2 giờ để xác nhận buổi học #{lesson.Lessonid}, nếu không hệ thống sẽ tự xác nhận.",
                    Type = NotificationType.LessonConfirmDeadline,
                    Referenceid = lesson.Lessonid.ToString()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Không thể gửi nhắc hạn xác nhận cho lesson {LessonId}.", lesson.Lessonid);
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
/// Background job for sending lesson reminders
/// Runs every 15 minutes
/// </summary>
public class LessonReminderJob : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LessonReminderJob> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(15);

    public LessonReminderJob(IServiceProvider serviceProvider, ILogger<LessonReminderJob> logger)
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
                _logger.LogError(ex, "Lỗi trong LessonReminderJob.");
            }

            await Task.Delay(_interval, stoppingToken);
        }

        _logger.LogInformation("LessonReminderJob dừng.");
    }

    private async Task SendRemindersAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MV.ApplicationLayer.Interfaces.IAppDbContext>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var zaloOAService = scope.ServiceProvider.GetRequiredService<IZaloOAService>();

        var now = TimeZoneHelper.UtcNow;
        var reminderWindow = now.AddMinutes(30);

        // Find lessons starting within 30 minutes that haven't been reminded
        var upcomingLessons = await context.Lessons
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

        if (!upcomingLessons.Any())
        {
            _logger.LogDebug("LessonReminderJob: Không có bài học nào sắp tới cần nhắc nhở.");
            return;
        }

        foreach (var lesson in upcomingLessons)
        {
            try
            {
                var subjectName = lesson.Booking?.Subject?.Subjectname ?? DisplayValues.NotAvailable;
                var minutesUntil = (int)(lesson.Scheduledstart - now).TotalMinutes;

                // Remind Tutor
                if (!string.IsNullOrEmpty(lesson.Tutorid))
                {
                    await notificationService.CreateNotificationAsync(new MV.DomainLayer.DTO.RequestModel.NotificationRequest
                    {
                        Userid = lesson.Tutorid,
                        Title = "Nhắc nhở buổi học",
                        Message = $"Buổi học môn {subjectName} sẽ bắt đầu trong {minutesUntil} phút. Hãy chuẩn bị sẵn sàng!",
                        Type = NotificationType.LessonReminder,
                        Referenceid = lesson.Lessonid.ToString()
                    });
                }

                // Remind Parent
                var parentId = lesson.Booking?.Student?.Parentid;
                if (!string.IsNullOrEmpty(parentId))
                {
                    await notificationService.CreateNotificationAsync(new MV.DomainLayer.DTO.RequestModel.NotificationRequest
                    {
                        Userid = parentId,
                        Title = "Nhắc nhở buổi học",
                        Message = $"Buổi học môn {subjectName} của con bạn sẽ bắt đầu trong {minutesUntil} phút.",
                        Type = NotificationType.LessonReminder,
                        Referenceid = lesson.Lessonid.ToString()
                    });

                    // Zalo ZNS reminder (only if user has Zalo linked + notifications enabled)
                    var vnTime = TimeZoneHelper.ToUserTime(lesson.Scheduledstart).ToString("HH:mm dd/MM");
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
                lesson.Autoreportsent = true;
                lesson.Autoreportsentat = now;

                _logger.LogInformation("Đã gửi lời nhắc nhở về bài học {LessonId} bắt đầu từ {Minutes} phút.", lesson.Lessonid, minutesUntil);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi gửi lời nhắc nhở cho bài học {LessonId}.", lesson.Lessonid);
            }
        }

        await context.SaveChangesAsync(ct);
        _logger.LogInformation("LessonReminderJob: Đã gửi lời nhắc cho {Count} bài học.", upcomingLessons.Count);
    }
}

/// <summary>
/// Background job that triggers remaining payment request after first lesson is confirmed (24h after report)
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
        // and have at least one lesson whose confirm deadline has passed (24h after report)
        // with no open disputes
        var bookingsToTrigger = await context.Bookings
            .Where(b => b.Status == BookingStatus.DepositPaid
                        && b.Remainingpaidat == null)
            .Include(b => b.Lessons)
            .Include(b => b.Student)
            .ToListAsync(ct);

        var triggeredCount = 0;

        foreach (var booking in bookingsToTrigger)
        {
            // Check if any lesson has passed its 24h confirmation deadline
            var confirmedLesson = booking.Lessons.FirstOrDefault(
                l => l.Status == PendingConfirmation
                    && l.Confirmdeadline.HasValue
                    && l.Confirmdeadline.Value <= now);

            // Also check for already-completed lessons
            var completedLesson = booking.Lessons.FirstOrDefault(l => l.Status == Completed);

            if (confirmedLesson == null && completedLesson == null)
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
                        Title = "Thanh toán 50% còn lại",
                        Message = $"Buổi học đầu tiên của booking #{booking.Bookingid} đã được xác nhận thành công. " +
                            $"Vui lòng thanh toán {booking.Remainingamount:N0}đ còn lại trong vòng 48h để tiếp tục các buổi học.",
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
                        Message = $"Hệ thống đã yêu cầu phụ huynh thanh toán 50% còn lại cho booking #{booking.Bookingid}.",
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
