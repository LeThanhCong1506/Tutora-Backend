using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Helpers;
using System.Data;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using MV.DomainLayer.Helpers;
using static MV.DomainLayer.Constants.PaymentStatus;

namespace MV.ApplicationLayer.Services;

public partial class PaymentService
{
    // ─── Wallet Payment ───────────────────────────────────────────────────

    public async Task PayWithWalletAsync(int bookingId, string userId, CancellationToken ct = default)
    {
        var booking = await bookingRepo.FindForPaymentByUserAsync(bookingId, userId, ct)
            ?? throw new BookingException(BookingErrorCodes.BookingNotFound, ApiMessages.BookingNotFound, 404);

        EnsureDepositAmountsCalculated(booking);

        bool isDepositPhase = booking.Depositpaidat == null;

        if (isDepositPhase)
        {
            if (booking.Status != BookingStatus.Accepted && booking.Status != BookingStatus.PendingPayment)
                throw new BookingException(BookingErrorCodes.InvalidBookingStatus, "Booking not ready for payment", 409);
            if (booking.Paymentdueat <= MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow)
                throw new BookingException(BookingErrorCodes.BookingExpired, "Booking payment expired", 409);
        }
        else
        {
            if (booking.Remainingpaidat != null)
                throw new BookingException(BookingErrorCodes.RemainingAlreadyPaid, "Phần thanh toán còn lại đã được thanh toán rồi", 409);
            if (booking.Status != BookingStatus.DepositPaid && booking.Status != BookingStatus.PendingRemainingPayment
                && booking.Status != BookingStatus.Ongoing)
                throw new BookingException(BookingErrorCodes.InvalidBookingStatus, "Booking not ready for remaining payment", 409);
        }

        if (booking.Paymentstatus == Escrowed || booking.Status == BookingStatus.Paid)
            throw new BookingException(BookingErrorCodes.BookingAlreadyPaid, "Booking already paid", 409);

        var amount = isDepositPhase
            ? (int)(booking.Depositamount ?? 0)
            : (int)(booking.Remainingamount ?? 0);

        if (amount <= 0)
            throw new BookingException(BookingErrorCodes.InvalidInput, "Số tiền booking không hợp lệ", 400);

        var txType = isDepositPhase ? TransactionType.DepositPayment : TransactionType.RemainingPayment;
        var description = isDepositPhase
            ? $"Deposit for booking #{bookingId}"
            : $"Remaining payment for booking #{bookingId}";

        await using var tx = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        try
        {
            var wallet = await walletRepo.GetByUserIdForUpdateAsync(userId, ct)
                ?? throw new BookingException(BookingErrorCodes.BookingNotFound, "Không tìm thấy ví người dùng", 404);

            if ((wallet.Balance ?? 0) < amount)
                throw new BookingException(WalletErrorCodes.InsufficientBalance, "Số dư ví không đủ, vui lòng nạp thêm tiền", 400);

            wallet.Balance = (wallet.Balance ?? 0) - amount;
            wallet.Lastupdated = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

            walletRepo.AddTransaction(new Wallettransaction
            {
                Wallet = wallet,
                Amount = -amount,
                Transactiontype = txType,
                Referencetable = ReferenceTable.Booking,
                Referenceid = bookingId,
                Description = description,
                Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
            });

            var txId = $"wallet-{bookingId}-{(isDepositPhase ? PaymentPhase.DepositShort : PaymentPhase.RemainingShort)}-{MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow:yyyyMMddHHmmss}";

            if (isDepositPhase)
            {
                booking.Status = BookingStatus.DepositPaid;
                booking.Paymentstatus = DepositEscrowed;
                booking.Depositpaidat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
                booking.Paymentdueat = null;
                booking.Escrowstatus = Holding;
                booking.Updatedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

                if (!string.IsNullOrWhiteSpace(booking.Tutorid))
                {
                    var tutorWallet = await walletRepo.GetOrCreateForUpdateAsync(booking.Tutorid, ct);
                    var totalEscrow = booking.Tutorfee ?? 0;
                    var depositEscrow = Math.Round(totalEscrow * 0.5m, 2);
                    tutorWallet.Frozenbalance = (tutorWallet.Frozenbalance ?? 0) + depositEscrow;
                    tutorWallet.Lastupdated = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

                    walletRepo.AddTransaction(new Wallettransaction
                    {
                        Wallet = tutorWallet,
                        Amount = depositEscrow,
                        Transactiontype = TransactionType.EscrowCredit,
                        Referencetable = ReferenceTable.Payment,
                        Referenceid = bookingId,
                        Description = txId,
                        Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
                    });
                }

            }
            else
            {
                booking.Status = BookingStatus.Paid;
                booking.Paymentstatus = Escrowed;
                booking.Remainingpaidat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
                booking.Paymentdueat = null;
                booking.Updatedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

                if (!string.IsNullOrWhiteSpace(booking.Tutorid))
                {
                    var tutorWallet = await walletRepo.GetOrCreateForUpdateAsync(booking.Tutorid, ct);
                    var totalEscrow = booking.Tutorfee ?? 0;
                    var depositEscrow = Math.Round(totalEscrow * 0.5m, 2);
                    var remainingEscrow = totalEscrow - depositEscrow;
                    tutorWallet.Frozenbalance = (tutorWallet.Frozenbalance ?? 0) + remainingEscrow;
                    tutorWallet.Lastupdated = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

                    walletRepo.AddTransaction(new Wallettransaction
                    {
                        Wallet = tutorWallet,
                        Amount = remainingEscrow,
                        Transactiontype = TransactionType.EscrowCredit,
                        Referencetable = ReferenceTable.Payment,
                        Referenceid = bookingId,
                        Description = txId,
                        Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
                    });
                }

            }

            await context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            await SendPaymentPhaseNotificationsAsync(booking, isDepositPhase);

            logger.LogInformation("Parent {ParentId} paid {Phase} for booking {BookingId} with wallet",
                userId, isDepositPhase ? PaymentPhase.Deposit : PaymentPhase.Remaining, bookingId);

            // Post-commit: auto-create lessons (deposit phase only)
            if (isDepositPhase)
            {
                try
                {
                    await lessonService.AutoCreateLessonsAsync(bookingId, ct);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to auto-create lessons for booking {BookingId} (wallet pay)", bookingId);
                }
            }
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<WalletSummaryResponse> GetTutorWalletSummaryAsync(string tutorId)
    {
        var wallet = await walletRepo.GetByUserIdAsNoTrackingAsync(tutorId);
        return new WalletSummaryResponse
        {
            Balance = wallet?.Balance ?? 0,
            FrozenBalance = wallet?.Frozenbalance ?? 0,
            LastUpdated = wallet != null && wallet.Lastupdated
        };
    }

    private async Task SendPaymentPhaseNotificationsAsync(Booking booking, bool isDepositPhase)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(booking.Parentid))
            {
                await notificationService.CreateNotificationAsync(new NotificationRequest
                {
                    Userid = booking.Parentid,
                    Title = isDepositPhase ? "Cọc thành công" : "Thanh toán hoàn tất",
                    Message = isDepositPhase
                        ? $"Đã thanh toán cọc 50% ({booking.Depositamount:N0}đ) cho booking #{booking.Bookingid}. Gia sư sẽ bắt đầu dạy buổi đầu tiên."
                        : $"Đã thanh toán 50% còn lại ({booking.Remainingamount:N0}đ) cho booking #{booking.Bookingid}. Booking đã được thanh toán đầy đủ.",
                    Type = NotificationType.PaymentSuccess,
                    Referenceid = booking.Bookingid.ToString()
                });
            }

            if (!string.IsNullOrWhiteSpace(booking.Tutorid))
            {
                await notificationService.CreateNotificationAsync(new NotificationRequest
                {
                    Userid = booking.Tutorid,
                    Title = isDepositPhase ? "Booking đã được cọc" : "Booking đã thanh toán đầy đủ",
                    Message = isDepositPhase
                        ? $"Phụ huynh đã cọc 50% cho booking #{booking.Bookingid}. Bạn có thể bắt đầu dạy."
                        : $"Booking #{booking.Bookingid} đã được thanh toán đầy đủ.",
                    Type = NotificationType.PaymentSuccess,
                    Referenceid = booking.Bookingid.ToString()
                });
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Không thể gửi thông báo thanh toán cho booking {BookingId}", booking.Bookingid);
        }
    }
}
