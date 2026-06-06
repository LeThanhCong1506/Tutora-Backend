using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.ApplicationLayer.Interfaces;
using MV.DomainLayer.Helpers;

namespace MV.ApplicationLayer.BackgroundJobs;

public class PaymentTimeoutJob(IServiceProvider sp, ILogger<PaymentTimeoutJob> logger) : BackgroundService
{
    private readonly TimeSpan _interval = TimeSpan.FromHours(12);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("PaymentTimeoutJob bắt đầu.");

        while (!ct.IsCancellationRequested)
        {
            try { await ProcessExpiredBookingsAsync(ct); }
            catch (Exception ex) { logger.LogError(ex, "Lỗi khi xử lý các đặt chỗ hết hạn."); }

            try { await ProcessExpiredRemainingPaymentsAsync(ct); }
            catch (Exception ex) { logger.LogError(ex, "Lỗi khi xử lý các khoản thanh toán còn lại hết hạn."); }

            await Task.Delay(_interval, ct);
        }
    }

    private async Task ProcessExpiredBookingsAsync(CancellationToken ct)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var notify = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var now = VietnamTimeHelper.UtcNow;

        var expired = await db.Bookings
            .Include(b => b.Chatchannels)
            .Where(b => (b.Status == BookingStatus.PendingPayment || b.Status == BookingStatus.Accepted)
                        && b.Paymentdueat != null
                        && b.Paymentdueat < now)
            .ToListAsync(ct);

        if (expired.Count == 0) return;

        logger.LogInformation("Found {Count} đặt chỗ đã hết hạn.", expired.Count);

        foreach (var b in expired)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                b.Status = BookingStatus.PaymentTimeout;
                b.Updatedat = now;
                foreach (var ch in b.Chatchannels) ch.Status = ChatChannelStatus.Closed;

                await db.SaveChangesAsync(ct);

                var notifications = new List<NotificationRequest>();
                if (!string.IsNullOrEmpty(b.Parentid))
                    notifications.Add(new NotificationRequest { Userid = b.Parentid, Title = "Booking đã hết hạn thanh toán", Message = $"Booking #{b.Bookingid} đã bị hủy do quá hạn thanh toán 24h." });
                if (!string.IsNullOrEmpty(b.Tutorid))
                    notifications.Add(new NotificationRequest { Userid = b.Tutorid, Title = "Booking đã hết hạn thanh toán", Message = $"Booking #{b.Bookingid} đã bị hủy do phụ huynh không thanh toán trong 24h." });

                if (notifications.Count > 0) await notify.CreateNotificationsAsync(notifications);

                await tx.CommitAsync(ct);
                logger.LogInformation("Đã xử lý đặt chỗ hết hạn {BookingId}.", b.Bookingid);
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                logger.LogError(ex, "Không thể xử lý đặt chỗ hết hạn {BookingId}.", b.Bookingid);
            }
        }
    }

    /// <summary>
    /// Handle expired remaining payment deadlines.
    /// Unlike deposit timeout (which cancels the booking), remaining timeout only sends reminders
    /// because the first lesson has already been taught. Check-in blocking handles enforcement.
    /// </summary>
    private async Task ProcessExpiredRemainingPaymentsAsync(CancellationToken ct)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var notify = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var now = VietnamTimeHelper.UtcNow;

        var expiredRemaining = await db.Bookings
            .Include(b => b.Student)
            .Where(b => b.Status == BookingStatus.PendingRemainingPayment
                        && b.Paymentdueat != null
                        && b.Paymentdueat < now
                        && b.Remainingpaidat == null)
            .ToListAsync(ct);

        if (expiredRemaining.Count == 0) return;

        logger.LogInformation("Thành lập {Count} đặt chỗ với thời hạn thanh toán còn lại đã hết hạn.", expiredRemaining.Count);

        foreach (var b in expiredRemaining)
        {
            try
            {
                // Extend deadline and send reminder - do NOT cancel
                b.Paymentdueat = now.AddHours(24);
                b.Updatedat = now;
                await db.SaveChangesAsync(ct);

                var notifications = new List<NotificationRequest>();
                var parentId = b.Student?.Parentid ?? b.Parentid;
                if (!string.IsNullOrEmpty(parentId))
                    notifications.Add(new NotificationRequest
                    {
                        Userid = parentId,
                        Title = "Nhắc nhở thanh toán 50% còn lại",
                        Message = $"Booking #{b.Bookingid} chưa được thanh toán đầy đủ. " +
                            $"Vui lòng thanh toán {b.Remainingamount:N0}đ còn lại để tiếp tục các buổi học."
                    });
                if (!string.IsNullOrEmpty(b.Tutorid))
                    notifications.Add(new NotificationRequest
                    {
                        Userid = b.Tutorid,
                        Title = "Phụ huynh chưa thanh toán phần còn lại",
                        Message = $"Booking #{b.Bookingid}: Phụ huynh chưa thanh toán 50% còn lại. Các buổi học tiếp theo đã bị tạm dừng."
                    });

                if (notifications.Count > 0) await notify.CreateNotificationsAsync(notifications);

                logger.LogInformation("Đã gửi nhắc nhở thanh toán phần còn lại cho đặt chỗ {BookingId}.", b.Bookingid);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Không thể xử lý nhắc nhở thanh toán phần còn lại cho đặt chỗ {BookingId}.", b.Bookingid);
            }
        }
    }
}
