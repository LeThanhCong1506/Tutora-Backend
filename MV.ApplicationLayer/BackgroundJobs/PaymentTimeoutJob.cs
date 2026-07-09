using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.ApplicationLayer.RepositoryInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.ApplicationLayer.Interfaces;
using MV.DomainLayer.Helpers;

namespace MV.ApplicationLayer.BackgroundJobs;

public class PaymentTimeoutJob(IServiceProvider sp, ILogger<PaymentTimeoutJob> logger) : BackgroundService
{
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("PaymentTimeoutJob bắt đầu.");

        while (!ct.IsCancellationRequested)
        {
            try { await ProcessUpcomingDepositDeadlinesAsync(ct); }
            catch (Exception ex) { logger.LogError(ex, "Lỗi khi xử lý các đặt chỗ sắp hết hạn thanh toán."); }

            try { await ProcessExpiredBookingsAsync(ct); }
            catch (Exception ex) { logger.LogError(ex, "Lỗi khi xử lý các đặt chỗ hết hạn."); }

            try { await ProcessExpiredRemainingPaymentsAsync(ct); }
            catch (Exception ex) { logger.LogError(ex, "Lỗi khi xử lý các khoản thanh toán còn lại hết hạn."); }

            await Task.Delay(_interval, ct);
        }
    }

    private async Task ProcessUpcomingDepositDeadlinesAsync(CancellationToken ct)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var notify = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var notificationRepo = scope.ServiceProvider.GetRequiredService<INotificationRepository>();
        var now = TimeZoneHelper.UtcNow;
        var dueSoon = now.AddMinutes(10);

        var bookingsDueSoon = await db.Bookings
            .Where(b => (b.Status == BookingStatus.PendingPayment || b.Status == BookingStatus.Accepted)
                        && b.Paymentdueat != null
                        && b.Paymentdueat > now
                        && b.Paymentdueat <= dueSoon)
            .ToListAsync(ct);

        foreach (var booking in bookingsDueSoon)
        {
            if (string.IsNullOrWhiteSpace(booking.Parentid))
                continue;

            var alreadySent = await notificationRepo.ExistsByUserAndTypeAndReferenceAsync(
                booking.Parentid,
                NotificationType.BookingPaymentDueSoon,
                booking.Bookingid.ToString());

            if (alreadySent)
                continue;

            try
            {
                await notify.CreateNotificationAsync(new NotificationRequest
                {
                    Userid = booking.Parentid,
                    Title = "Sắp hết hạn thanh toán buổi học đầu tiên",
                    Message = $"Booking #{booking.Bookingid} sắp hết hạn thanh toán buổi học đầu tiên. Vui lòng hoàn tất thanh toán để giữ lịch học.",
                    Type = NotificationType.BookingPaymentDueSoon,
                    Referenceid = booking.Bookingid.ToString()
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Không thể gửi cảnh báo sắp hết hạn thanh toán cho booking {BookingId}.", booking.Bookingid);
            }
        }
    }

    private async Task ProcessExpiredBookingsAsync(CancellationToken ct)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var notify = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var now = TimeZoneHelper.UtcNow;

        var expired = await db.Bookings
            .Include(b => b.Chatchannels)
            .Include(b => b.ClassSessions)
            .Where(b => (b.Status == BookingStatus.PendingPayment || b.Status == BookingStatus.Accepted)
                        && b.Paymentdueat != null
                        && b.Paymentdueat < now)
            .ToListAsync(ct);

        if (expired.Count == 0) return;

        logger.LogInformation("Found {Count} đặt chỗ đã hết hạn.", expired.Count);

        foreach (var b in expired)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            List<NotificationRequest> notifications = new();
            try
            {
                b.Status = BookingStatus.PaymentTimeout;
                b.Updatedat = now;
                foreach (var ch in b.Chatchannels) ch.Status = ChatChannelStatus.Closed;
                foreach (var classSession in b.ClassSessions.Where(l => l.Status == ClassSessionStatus.Reserved))
                    classSession.Status = ClassSessionStatus.Cancelled;

                // Return the promotion usage consumed at booking creation
                await MV.ApplicationLayer.Helpers.PromotionUsageHelper.ReturnUsageAsync(db, b.Promotionid, ct);

                await db.SaveChangesAsync(ct);

                if (!string.IsNullOrEmpty(b.Parentid))
                    notifications.Add(new NotificationRequest
                    {
                        Userid = b.Parentid,
                        Title = "Booking đã hết hạn thanh toán",
                        Message = $"Booking #{b.Bookingid} đã bị hủy do quá hạn thanh toán 30 phút.",
                        Type = NotificationType.BookingTimeout,
                        Referenceid = b.Bookingid.ToString()
                    });
                if (!string.IsNullOrEmpty(b.Tutorid))
                    notifications.Add(new NotificationRequest
                    {
                        Userid = b.Tutorid,
                        Title = "Booking đã hết hạn thanh toán",
                        Message = $"Booking #{b.Bookingid} đã bị hủy do phụ huynh không thanh toán trong 30 phút.",
                        Type = NotificationType.BookingTimeout,
                        Referenceid = b.Bookingid.ToString()
                    });

                await tx.CommitAsync(ct);
                logger.LogInformation("Đã xử lý đặt chỗ hết hạn {BookingId}.", b.Bookingid);
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                logger.LogError(ex, "Không thể xử lý đặt chỗ hết hạn {BookingId}.", b.Bookingid);
                continue;
            }

            if (notifications.Count > 0)
            {
                try
                {
                    await notify.CreateNotificationsAsync(notifications);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Không thể gửi thông báo hết hạn cho booking {BookingId}.", b.Bookingid);
                }
            }
        }
    }

    /// <summary>
    /// Handle expired remaining payment deadlines.
    /// Parent chose not to pay for remaining sessions within 48h → finalize booking early:
    /// release escrow for completed classSessions, cancel remaining classSessions, mark booking Completed.
    /// </summary>
    private async Task ProcessExpiredRemainingPaymentsAsync(CancellationToken ct)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var settlement = scope.ServiceProvider.GetRequiredService<ISettlementService>();
        var now = TimeZoneHelper.UtcNow;

        var expiredRemaining = await db.Bookings
            .Where(b => b.Status == BookingStatus.PendingRemainingPayment
                        && b.Paymentdueat != null
                        && b.Paymentdueat < now
                        && b.Remainingpaidat == null)
            .Select(b => b.Bookingid)
            .ToListAsync(ct);

        if (expiredRemaining.Count == 0) return;

        logger.LogInformation("{Count} booking hết hạn thanh toán buổi còn lại — tiến hành kết thúc sớm.", expiredRemaining.Count);

        foreach (var bookingId in expiredRemaining)
        {
            try
            {
                await settlement.FinalizeBookingEarlyAsync(bookingId, ct);
                logger.LogInformation("Booking {BookingId} đã kết thúc sớm do hết hạn thanh toán.", bookingId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Không thể kết thúc sớm booking {BookingId}.", bookingId);
            }
        }
    }
}
