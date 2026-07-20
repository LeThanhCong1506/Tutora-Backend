using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Helpers;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;

namespace MV.ApplicationLayer.Services;

public partial class PaymentService
{
    public async Task<TopupResponse> CreateBookingShortfallTopupAsync(
        int bookingId,
        string userId,
        CancellationToken ct = default)
    {
        var summary = await GetPaymentSummaryAsync(bookingId, userId);
        if (summary.CanPayWithWallet)
        {
            throw new BookingException(
                WalletErrorCodes.MinimumTopupRequired,
                "Số dư ví đã đủ để thanh toán booking.",
                409);
        }

        var lockKey = $"wallet-shortfall:{bookingId}:{summary.PaymentPhase}:{userId}";
        var gate = PaymentRequestCreationLocks.GetOrAdd(
            lockKey,
            _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(ct);
        try
        {
            return await CreateBookingShortfallTopupCoreAsync(
                bookingId,
                userId,
                summary.PaymentPhase,
                ct);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ApplyBookingShortfallTopupAsync(
        int bookingId,
        long orderCode,
        string userId,
        CancellationToken ct = default)
    {
        var topup = await context.Topuprequests.AsNoTracking()
            .FirstOrDefaultAsync(t =>
                t.Bookingid == bookingId
                && t.Ordercode == orderCode
                && t.Userid == userId,
                ct)
            ?? throw new BookingException(
                WalletErrorCodes.TopupNotFound,
                "Không tìm thấy yêu cầu nạp bù của booking.",
                404);

        if (topup.Status != TopupStatus.Completed)
        {
            throw new BookingException(
                WalletErrorCodes.TopupNotFound,
                "Khoản nạp bù chưa được cộng vào ví.",
                409);
        }

        var walletCredited = await context.Wallettransactions.AsNoTracking()
            .AnyAsync(t =>
                t.Ordercode == orderCode
                && t.Referencetable == ReferenceTable.Topup,
                ct);
        if (!walletCredited)
        {
            throw new BookingException(
                WalletErrorCodes.TopupNotFound,
                "Khoản nạp bù đang được đối soát, vui lòng thử lại.",
                409);
        }

        if (topup.Paymentphase is not (
            PaymentRequestPhase.Deposit or PaymentRequestPhase.Remaining))
        {
            throw new BookingException(
                BookingErrorCodes.InvalidBookingStatus,
                "Yêu cầu nạp bù không còn khớp với giai đoạn thanh toán.",
                409);
        }

        await PayWithWalletCoreAsync(
            bookingId,
            userId,
            topup.Paymentphase,
            ct);
    }

    private async Task<TopupResponse> CreateBookingShortfallTopupCoreAsync(
        int bookingId,
        string userId,
        string paymentPhase,
        CancellationToken ct)
    {
        Topuprequest topup;

        await using (var tx = await context.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            ct))
        {
            try
            {
                var validated = await LockAndValidateBookingForPaymentRequestAsync(
                    bookingId,
                    userId,
                    paymentPhase,
                    ct);
                var wallet = await walletRepo.GetByUserIdForUpdateAsync(userId, ct);
                var topupAmount = WalletShortfallTopupCalculator.Calculate(
                    validated.Amount,
                    wallet?.Balance ?? 0);

                if (topupAmount <= 0)
                {
                    throw new BookingException(
                        WalletErrorCodes.MinimumTopupRequired,
                        "Số dư ví đã đủ để thanh toán booking.",
                        409);
                }

                var orderCode = await GenerateUniqueTopupOrderCodeAsync(userId, ct);
                var now = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
                var maximumExpiry = now.AddHours(24);
                var expiresAt = validated.ExpiresAt ?? maximumExpiry;
                if (expiresAt > maximumExpiry)
                    expiresAt = maximumExpiry;

                topup = new Topuprequest
                {
                    Bookingid = bookingId,
                    Paymentphase = paymentPhase,
                    Ordercode = orderCode,
                    Userid = userId,
                    Amount = topupAmount,
                    Status = TopupStatus.Pending,
                    Createdat = now,
                    Expiresat = expiresAt
                };

                context.Topuprequests.Add(topup);
                await context.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        }

        try
        {
            var expiredAtUnix = (int)new DateTimeOffset(
                DateTime.SpecifyKind(
                    topup.Expiresat ?? MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow.AddHours(24),
                    DateTimeKind.Utc))
                .ToUnixTimeSeconds();
            var link = await _linkFactory.CreatePaymentLink(
                topup.Ordercode,
                checked((int)(topup.Amount ?? 0)),
                $"Bu booking #{bookingId}",
                expiredAtUnix);

            topup.Paymentlinkid = link.PaymentLinkId;
            await context.SaveChangesAsync(ct);

            logger.LogInformation(
                "Created booking shortfall top-up {OrderCode} for booking {BookingId}, phase {Phase}, user {UserId}, amount {Amount}",
                topup.Ordercode,
                bookingId,
                paymentPhase,
                userId,
                topup.Amount);

            return new TopupResponse
            {
                PaymentLinkId = link.PaymentLinkId,
                OrderCode = topup.Ordercode,
                Amount = topup.Amount ?? 0,
                Currency = link.Currency ?? Currency.Vnd,
                CheckoutUrl = link.CheckoutUrl,
                QrCode = link.QrCode,
                AccountNumber = link.AccountNumber ?? "",
                AccountName = link.AccountName ?? "",
                Bin = link.Bin ?? "",
                Description = link.Description ?? "",
                ExpiredAt = link.ExpiredAt.HasValue
                    ? DateTimeOffset.FromUnixTimeSeconds(link.ExpiredAt.Value).UtcDateTime
                    : topup.Expiresat
            };
        }
        catch (OperationCanceledException)
            when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            topup.Status = TopupStatus.Failed;
            try
            {
                await context.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception persistException)
            {
                logger.LogError(
                    persistException,
                    "Could not mark failed booking shortfall top-up {OrderCode}",
                    topup.Ordercode);
            }

            logger.LogError(
                ex,
                "Failed to create booking shortfall top-up for booking {BookingId}",
                bookingId);
            throw new BookingException(
                WalletErrorCodes.InvalidAmount,
                "Tạo mã nạp bù thất bại. Vui lòng thử lại.",
                500);
        }
    }

    private async Task<long> GenerateUniqueTopupOrderCodeAsync(
        string userId,
        CancellationToken ct)
    {
        for (var attempt = 0; attempt < 32; attempt++)
        {
            var candidate = OrderCodeHelper.GenerateTopupOrderCode(userId);
            if (!await context.Topuprequests.AsNoTracking()
                .AnyAsync(t => t.Ordercode == candidate, ct))
            {
                return candidate;
            }
        }

        throw new BookingException(
            BookingErrorCodes.InvalidInput,
            "Không thể cấp mã PayOS duy nhất cho yêu cầu nạp bù.",
            409);
    }
}
