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
        var now = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

        int[] expiredIds;
        using (var discoveryScope = sp.CreateScope())
        {
            var discoveryDb = discoveryScope.ServiceProvider.GetRequiredService<IAppDbContext>();
            expiredIds = await discoveryDb.Bookings
                .AsNoTracking()
                .Where(b => b.Status == BookingStatus.PendingTutor
                            && b.Responsedeadline != null
                            && b.Responsedeadline <= now)
                .Select(b => b.Bookingid)
                .ToArrayAsync(ct);
        }

        if (expiredIds.Length == 0) return;

        logger.LogInformation("Tìm thấy {Count} booking đã hết hạn phản hồi của gia sư.", expiredIds.Length);

        foreach (var bookingId in expiredIds)
            await ProcessExpiredResponseAsync(bookingId, ct);
    }

    private async Task ProcessExpiredResponseAsync(int bookingId, CancellationToken ct)
    {
        // A fresh scope per booking prevents a rolled-back entity graph from leaking into the next item.
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var notify = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var now = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
        List<NotificationRequest> notifications = new();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            // Lock and re-read the booking inside the transaction. Multiple app instances and tutor
            // decisions now serialize on this row, so only one actor can perform the terminal action.
            var b = await db.Bookings
                .FromSqlRaw(SqlQueries.LockBookingById, bookingId)
                .SingleOrDefaultAsync(ct);

            if (b == null)
            {
                await tx.CommitAsync(ct);
                return;
            }

            if (!TutorResponseTimeoutPolicy.CanProcess(b, now))
            {
                if (b.Status == BookingStatus.PendingTutor && b.Refundstatus == RefundStatus.Refunded)
                {
                    logger.LogWarning(
                        "Bỏ qua timeout booking {BookingId}: booking vẫn pending_tutor nhưng đã được đánh dấu refunded.",
                        bookingId);
                }
                await tx.CommitAsync(ct);
                return;
            }

            b.Status = BookingStatus.Cancelled;
            b.Cancelledby = SystemActors.System;
            b.Cancelledat = now;
            b.Cancellationreason = "Gia sư không phản hồi trong 24 giờ";
            b.Updatedat = now;
            b.Responsedeadline = null;

            var chatChannels = await db.Chatchannels
                .Where(ch => ch.Bookingid == bookingId)
                .ToListAsync(ct);
            foreach (var ch in chatChannels)
                ch.Status = ChatChannelStatus.Closed;

            var reservedSessions = await db.ClassSessions
                .Where(classSession => classSession.Bookingid == bookingId
                                       && classSession.Status == ClassSessionStatus.Reserved)
                .ToListAsync(ct);
            foreach (var classSession in reservedSessions)
                classSession.Status = ClassSessionStatus.Cancelled;

            await RefundPaidBookingAsync(db, b, now, ct);
            await PromotionUsageHelper.ReturnUsageAsync(db, b.Promotionid, ct);
            await db.SaveChangesAsync(ct);

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
            logger.LogError(ex, "Không thể tự động hủy booking {BookingId}.", bookingId);
            return;
        }

        if (notifications.Count == 0) return;

        try
        {
            await notify.CreateNotificationsAsync(notifications);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Không thể gửi thông báo timeout cho booking {BookingId}.", bookingId);
        }
    }

    private static async Task RefundPaidBookingAsync(IAppDbContext db, Booking booking, DateTime now, CancellationToken ct)
    {
        if (booking.Depositpaidat == null)
            throw new InvalidOperationException(
                $"Booking #{booking.Bookingid} is pending tutor response without a paid deposit.");

        var refundAmount = TutorResponseTimeoutPolicy.ParentRefundAmount(booking);

        if (refundAmount <= 0 || string.IsNullOrWhiteSpace(booking.Parentid))
            throw new InvalidOperationException(
                $"Booking #{booking.Bookingid} has no valid refund recipient or amount.");

        var parentWallet = await WalletLockHelper.GetOrCreateForUpdateAsync(db, booking.Parentid, now, ct);
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

        var tutorEscrowAmount = TutorResponseTimeoutPolicy.TutorEscrowAmount(booking);

        if (tutorEscrowAmount > 0)
        {
            if (string.IsNullOrWhiteSpace(booking.Tutorid))
                throw new InvalidOperationException($"Booking #{booking.Bookingid} has escrow but no tutor id.");

            var tutorWallet = await WalletLockHelper.GetRequiredForUpdateAsync(db, booking.Tutorid, ct);
            if ((tutorWallet.Frozenbalance ?? 0) < tutorEscrowAmount)
                throw new InvalidOperationException(
                    $"Tutor escrow balance is insufficient for booking #{booking.Bookingid}.");

            tutorWallet.Frozenbalance = (tutorWallet.Frozenbalance ?? 0) - tutorEscrowAmount;
            tutorWallet.Lastupdated = now;

                db.Wallettransactions.Add(new Wallettransaction
                {
                    Wallet = tutorWallet,
                    Amount = -tutorEscrowAmount,
                    Transactiontype = TransactionType.EscrowReversal,
                    Referencetable = ReferenceTable.Booking,
                    Referenceid = booking.Bookingid,
                    Description = $"Giải phóng escrow booking #{booking.Bookingid} do gia sư không phản hồi",
                    Createdat = now
                });
            }
        }

        booking.Refundamount = refundAmount;
        booking.Refundstatus = RefundStatus.Refunded;
        booking.Escrowstatus = EscrowStatus.Refunded;
    }
}
