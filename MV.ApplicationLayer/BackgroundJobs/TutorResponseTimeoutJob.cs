using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.Helpers;
using MV.DomainLayer.Entities;

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
            .Include(b => b.ClassSessions)
            .Where(b => b.Status == BookingStatus.PendingTutor
                        && b.Responsedeadline != null
                        && b.Responsedeadline < now)
            .ToListAsync(ct);

        if (expired.Count == 0) return;

        logger.LogInformation("Thành lập {Count} bookings với thời hạn phản hồi của gia sư đã hết hạn.", expired.Count);

        foreach (var b in expired)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            List<NotificationRequest> notifications = new();
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
                foreach (var classSession in b.ClassSessions.Where(l => l.Status == ClassSessionStatus.Reserved))
                    classSession.Status = ClassSessionStatus.Cancelled;

                await RefundPaidBookingAsync(db, b, now, ct);

                // Return the promotion usage consumed at booking creation
                await MV.ApplicationLayer.Helpers.PromotionUsageHelper.ReturnUsageAsync(db, b.Promotionid, ct);

                await db.SaveChangesAsync(ct);

                // Notify parent/student
                if (!string.IsNullOrEmpty(b.Parentid))
                {
                    notifications.Add(new NotificationRequest
                    {
                        Userid = b.Parentid,
                        Title = "Booking đã tự động hủy",
                        Message = $"Booking #{b.Bookingid} đã bị hủy do gia sư không phản hồi trong 24 giờ. Tiền cọc đã được hoàn vào ví của bạn.",
                        Type = NotificationType.BookingTimeout,
                        Referenceid = b.Bookingid.ToString()
                    });
                }

                // Notify tutor
                if (!string.IsNullOrEmpty(b.Tutorid))
                {
                    notifications.Add(new NotificationRequest
                    {
                        Userid = b.Tutorid,
                        Title = "Booking đã bị hủy",
                        Message = $"Booking #{b.Bookingid} đã bị hủy do bạn không phản hồi trong 24 giờ.",
                        Type = NotificationType.BookingTimeout,
                        Referenceid = b.Bookingid.ToString()
                    });
                }

                await tx.CommitAsync(ct);
                logger.LogInformation("Booking {BookingId} đã tự động hủy do gia sư không phản hồi trong 24 giờ.", b.Bookingid);
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                logger.LogError(ex, "Không thể tự động hủy booking {BookingId}.", b.Bookingid);
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
                    logger.LogError(ex, "Không thể gửi thông báo timeout cho booking {BookingId}.", b.Bookingid);
                }
            }
        }
    }

    private static async Task RefundPaidBookingAsync(IAppDbContext db, Booking booking, DateTime now, CancellationToken ct)
    {
        if (booking.Depositpaidat == null)
        {
            booking.Refundstatus = RefundStatus.NoRefund;
            return;
        }

        var refundAmount = booking.Paymentstatus == PaymentStatus.Escrowed || booking.Remainingpaidat != null
            ? booking.Finalprice ?? booking.Totalamount ?? 0
            : booking.Depositamount ?? 0;

        if (refundAmount <= 0 || string.IsNullOrWhiteSpace(booking.Parentid))
        {
            booking.Refundstatus = RefundStatus.NoRefund;
            return;
        }

        var parentWallet = await db.Wallets
            .FromSqlRaw(SqlQueries.LockWalletByUserId, booking.Parentid)
            .FirstOrDefaultAsync(ct);

        if (parentWallet != null)
        {
            parentWallet.Balance = (parentWallet.Balance ?? 0) + refundAmount;
            parentWallet.Lastupdated = now;

            db.Wallettransactions.Add(new Wallettransaction
            {
                Wallet = parentWallet,
                Amount = refundAmount,
                Transactiontype = TransactionType.Refund,
                Referencetable = ReferenceTable.Booking,
                Referenceid = booking.Bookingid,
                Description = $"Hoàn tiền booking #{booking.Bookingid} do gia sư không phản hồi",
                Createdat = now
            });
        }

        if (!string.IsNullOrWhiteSpace(booking.Tutorid))
        {
            var tutorWallet = await db.Wallets
                .FromSqlRaw(SqlQueries.LockWalletByUserId, booking.Tutorid)
                .FirstOrDefaultAsync(ct);

            if (tutorWallet != null)
            {
                var tutorEscrowAmount = booking.Paymentstatus == PaymentStatus.Escrowed || booking.Remainingpaidat != null
                    ? booking.Tutorfee ?? 0
                    : Math.Round((booking.Tutorfee ?? 0) / Math.Max(booking.Totalsessions ?? 1, 1), 2);

                tutorWallet.Frozenbalance = Math.Max(0, (tutorWallet.Frozenbalance ?? 0) - tutorEscrowAmount);
                tutorWallet.Lastupdated = now;

                db.Wallettransactions.Add(new Wallettransaction
                {
                    Wallet = tutorWallet,
                    Amount = -tutorEscrowAmount,
                    Transactiontype = TransactionType.EscrowRelease,
                    Referencetable = ReferenceTable.Booking,
                    Referenceid = booking.Bookingid,
                    Description = $"Giải phóng escrow booking #{booking.Bookingid} do gia sư không phản hồi",
                    Createdat = now
                });
            }
        }

        booking.Refundamount = refundAmount;
        booking.Refundstatus = RefundStatus.Refunded;
    }
}
