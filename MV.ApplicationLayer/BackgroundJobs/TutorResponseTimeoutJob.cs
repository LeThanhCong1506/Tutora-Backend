using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.ApplicationLayer.Interfaces;

namespace MV.ApplicationLayer.BackgroundJobs;

/// <summary>
/// Phase 13: Background job that auto-cancels bookings if tutor doesn't respond within 24h.
/// Checks every hour for bookings in "pending_tutor" status whose response deadline has passed.
/// </summary>
public class TutorResponseTimeoutJob(IServiceProvider sp, ILogger<TutorResponseTimeoutJob> logger) : BackgroundService
{
    private readonly TimeSpan _interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("Công việc TutorResponseTimeoutJob đã bắt đầu — đang kiểm tra mọi thứ {Interval}.", _interval);
        // Wait for app to fully start before first execution
        await Task.Delay(TimeSpan.FromSeconds(30), ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ProcessExpiredResponsesAsync(ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Lỗi khi xử lý thời gian chờ phản hồi của gia sư.");
            }

            await Task.Delay(_interval, ct);
        }
    }

    private async Task ProcessExpiredResponsesAsync(CancellationToken ct)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var notify = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var now = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

        // Find bookings in pending_tutor status whose response deadline has passed
        var expired = await db.Bookings
            .Include(b => b.Chatchannels)
            .Where(b => b.Status == BookingStatus.PendingTutor
                        && b.Responsedeadline != null
                        && b.Responsedeadline < now)
            .ToListAsync(ct);

        if (expired.Count == 0) return;

        logger.LogInformation("Thành lập {Count} bookings với thời hạn phản hồi của gia sư đã hết hạn.", expired.Count);

        foreach (var b in expired)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                // Cancel the booking
                b.Status = BookingStatus.Cancelled;
                    b.Cancelledby = SystemActors.System;
                b.Cancelledat = now;
                b.Cancellationreason = "Gia sư không phản hồi trong 24 giờ";
                b.Updatedat = now;

                // Close associated chat channels
                foreach (var ch in b.Chatchannels)
                    ch.Status = ChatChannelStatus.Closed;

                await db.SaveChangesAsync(ct);

                // Notify parent/student
                var notifications = new List<NotificationRequest>();
                if (!string.IsNullOrEmpty(b.Parentid))
                {
                    notifications.Add(new NotificationRequest
                    {
                        Userid = b.Parentid,
                        Title = "Booking đã tự động hủy",
                        Message = $"Booking #{b.Bookingid} đã bị hủy do gia sư không phản hồi trong 24 giờ. Bạn có thể tìm gia sư khác."
                    });
                }

                // Notify tutor
                if (!string.IsNullOrEmpty(b.Tutorid))
                {
                    notifications.Add(new NotificationRequest
                    {
                        Userid = b.Tutorid,
                        Title = "Booking đã bị hủy",
                        Message = $"Booking #{b.Bookingid} đã bị hủy do bạn không phản hồi trong 24 giờ."
                    });
                }

                if (notifications.Count > 0)
                    await notify.CreateNotificationsAsync(notifications);

                await tx.CommitAsync(ct);
                logger.LogInformation("Booking {BookingId} đã tự động hủy do gia sư không phản hồi trong 24 giờ.", b.Bookingid);
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                logger.LogError(ex, "Không thể tự động hủy booking {BookingId}.", b.Bookingid);
            }
        }
    }
}
