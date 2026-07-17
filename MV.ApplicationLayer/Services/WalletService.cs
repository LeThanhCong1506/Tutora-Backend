using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Helpers;
using MV.ApplicationLayer.ServiceInterfaces;
using PayOS;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using MV.DomainLayer.Helpers;
using MV.ApplicationLayer.Interfaces;

namespace MV.ApplicationLayer.Services;

public class WalletService(
    IAppDbContext context,
    [FromKeyedServices(ServiceKeys.PayOS.Checkout)] PayOSClient payOS,
    ILogger<WalletService> logger) : IWalletService
{
    private readonly PayOSClient _payOS = payOS;

    public async Task ProcessTopupWebhookAsync(
        PaymentWebhookRequest request,
        string rawPayload,
        CancellationToken ct = default)
    {
        if (request?.Data == null)
            throw new BookingException(WalletErrorCodes.InvalidAmount, "Dữ liệu webhook không hợp lệ", 400);

        if (request.Code != PayOSWebhookCode.SuccessCode || !request.Success)
        {
            logger.LogWarning("Non-success topup webhook: code={Code}", request.Code);
            return;
        }

        var data = request.Data;
        logger.LogInformation("Processing topup webhook orderCode: {OrderCode}, amount: {Amount}", data.OrderCode, data.Amount);

        await using var tx = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        try
        {
            var topup = await context.Topuprequests
                .FirstOrDefaultAsync(t => t.Ordercode == data.OrderCode, ct)
                ?? throw new BookingException(WalletErrorCodes.TopupNotFound, "Không tìm thấy yêu cầu nạp bù của booking.", 404);

            if (topup.Bookingid is null
                || topup.Paymentphase is not (
                    PaymentRequestPhase.Deposit or PaymentRequestPhase.Remaining)
                || string.IsNullOrWhiteSpace(topup.Userid)
                || !topup.Amount.HasValue
                || topup.Amount.Value <= 0)
            {
                throw new BookingException(
                    WalletErrorCodes.TopupNotFound,
                    "Top-up truyền thống không còn được hỗ trợ.",
                    409);
            }

            if (data.Amount != (int)(topup.Amount ?? 0))
                throw new BookingException(WalletErrorCodes.AmountMismatch, "Số tiền không khớp", 409);

            var capture = PaymentTransactionCapture.FromPayOSWebhook(
                request,
                rawPayload);
            var providerTransactionId = capture.GetProviderTransactionId(data.Reference);

            var walletTransactionExists = await context.Wallettransactions
                .AsNoTracking()
                .AnyAsync(w => w.Ordercode == data.OrderCode
                    && w.Referencetable == ReferenceTable.Topup, ct);

            var paymentTransactionExists = !string.IsNullOrWhiteSpace(providerTransactionId)
                ? await context.PaymentTransactions.AsNoTracking().AnyAsync(t =>
                    t.Paymentmethod == PaymentTransactionMethod.PayOS
                    && t.Providertransactionid == providerTransactionId, ct)
                : await context.PaymentTransactions.AsNoTracking().AnyAsync(t =>
                    t.Purpose == PaymentTransactionPurpose.WalletTopup
                    && t.Ordercode == data.OrderCode, ct);

            if (topup.Status == TopupStatus.Completed
                && walletTransactionExists
                && paymentTransactionExists)
            {
                await tx.CommitAsync(ct);
                logger.LogInformation(
                    "Booking shortfall top-up {OrderCode} was already fully processed",
                    data.OrderCode);
                return;
            }

            if (!walletTransactionExists && topup.Status != TopupStatus.Completed)
            {
                var wallet = await context.Wallets
                    .FromSqlRaw(SqlQueries.LockWalletByUserId, topup.Userid)
                    .FirstOrDefaultAsync(ct)
                    ?? new Wallet
                    {
                        Userid = topup.Userid,
                        Balance = 0,
                        Frozenbalance = 0,
                        Lastupdated = TimeZoneHelper.UtcNow
                    };

                if (wallet.Walletid == 0)
                    context.Wallets.Add(wallet);

                wallet.Balance = (wallet.Balance ?? 0) + topup.Amount;
                wallet.Lastupdated = TimeZoneHelper.UtcNow;

                context.Wallettransactions.Add(new Wallettransaction
                {
                    Wallet = wallet,
                    Amount = topup.Amount,
                    Transactiontype = TransactionType.Deposit,
                    Referencetable = ReferenceTable.Topup,
                    Referenceid = topup.Topuprequestid,
                    Description = data.Reference,
                    Ordercode = data.OrderCode,
                    Createdat = TimeZoneHelper.UtcNow
                });
            }
            else if (!walletTransactionExists)
            {
                logger.LogWarning(
                    "Completed booking shortfall top-up {OrderCode} has no wallet ledger row; skipping balance mutation to avoid a double credit",
                    data.OrderCode);
            }

            if (topup.Status != TopupStatus.Completed)
            {
                topup.Status = TopupStatus.Completed;
                topup.Completedat = TimeZoneHelper.UtcNow;
            }

            if (!paymentTransactionExists)
            {
                context.PaymentTransactions.Add(capture.Create(
                    PaymentTransactionPurpose.WalletTopup,
                    PaymentTransactionDirection.Inbound,
                    topup.Amount ?? data.Amount,
                    topup.Userid,
                    data.OrderCode,
                    data.Reference,
                    bookingId: topup.Bookingid,
                    description: "Booking shortfall wallet top-up payment"));
            }

            await context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            logger.LogInformation(
                "Booking shortfall top-up {OrderCode} reconciled for user {UserId}; walletLedgerExisted={WalletLedgerExisted}, paymentAuditExisted={PaymentAuditExisted}",
                data.OrderCode,
                topup.Userid,
                walletTransactionExists,
                paymentTransactionExists);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<WalletBalanceResponse> GetWalletBalanceAsync(string userId)
    {
        var w = await context.Wallets.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Userid == userId);

        var bal = w?.Balance ?? 0;
        var frz = w?.Frozenbalance ?? 0;

        return new WalletBalanceResponse
        {
            Balance = bal,
            AvailableBalance = bal,
            FrozenBalance = frz,
            TotalBalance = bal + frz,
            LastUpdated = w != null ? w.Lastupdated : null
        };
    }

    public async Task<TransactionHistoryPagedResponse> GetTransactionHistoryAsync(string userId, int page = 1, int pageSize = 20)
    {
        var w = await context.Wallets.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Userid == userId);

        if (w == null)
            return new TransactionHistoryPagedResponse
            {
                Transactions = [],
                TotalCount = 0,
                Page = page,
                PageSize = pageSize
            };

        var query = context.Wallettransactions.AsNoTracking()
            .Where(t => t.Walletid == w.Walletid)
            .OrderByDescending(t => t.Createdat);

        var total = await query.CountAsync();
        var rawTxs = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new { t.Transactionid, t.Amount, t.Transactiontype, t.Description, t.Referenceid, t.Referencetable, t.Createdat })
            .ToListAsync();

        var txs = rawTxs.Select(t => new TransactionHistoryResponse
        {
            TransactionId = t.Transactionid,
            Amount = t.Amount ?? 0,
            TransactionType = t.Transactiontype ?? "",
            Description = t.Description ?? "",
            ReferenceId = t.Referenceid,
            ReferenceTable = t.Referencetable,
            CreatedAt = t.Createdat ?? MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
        }).ToList();

        return new TransactionHistoryPagedResponse
        {
            Transactions = txs,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<TopupStatusResponse> GetBookingShortfallTopupStatusAsync(
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

        if (topup.Status == TopupStatus.Completed)
        {
            return await BuildTopupStatusResponseAsync(
                topup,
                TopupStatus.Completed,
                ct);
        }

        if (string.IsNullOrWhiteSpace(topup.Paymentlinkid))
        {
            return await BuildTopupStatusResponseAsync(
                topup,
                topup.Status ?? TopupStatus.Pending,
                ct);
        }

        try
        {
            var info = await _payOS.PaymentRequests.GetAsync(topup.Paymentlinkid);
            var payosStatus = info.Status.ToString();

            if (payosStatus.Equals(
                PayOSLinkStatus.Paid,
                StringComparison.OrdinalIgnoreCase))
            {
                var capture = PaymentTransactionCapture
                    .FromPayOSPaymentLink(info)
                    .FirstOrDefault(c => c.ObservedAmount == (topup.Amount ?? 0));

                if (capture != null)
                {
                    var synthetic = new PaymentWebhookRequest
                    {
                        Code = PayOSWebhookCode.SuccessCode,
                        Desc = PayOSWebhookCode.SuccessDesc,
                        Success = true,
                        Data = new PayOSWebhookData
                        {
                            OrderCode = orderCode,
                            Amount = checked((int)(topup.Amount ?? 0)),
                            Reference = capture.ProviderTransactionId
                                ?? $"payos-link-{info.Id}",
                            PaymentLinkId = topup.Paymentlinkid,
                            Description = capture.Description
                                ?? $"Bu booking #{bookingId}",
                            TransactionDateTime = capture.PaidAtUtc?.ToString("O")
                                ?? "",
                            Currency = capture.Currency ?? Currency.Vnd
                        }
                    };

                    await ProcessTopupWebhookAsync(
                        synthetic,
                        System.Text.Json.JsonSerializer.Serialize(info),
                        ct);
                }
                else
                {
                    logger.LogWarning(
                        "Paid PayOS link {PaymentLinkId} for booking shortfall top-up {OrderCode} has no exact transaction capture yet",
                        topup.Paymentlinkid,
                        orderCode);
                }
            }

            var refreshed = await context.Topuprequests.AsNoTracking()
                .FirstOrDefaultAsync(t =>
                    t.Bookingid == bookingId
                    && t.Ordercode == orderCode
                    && t.Userid == userId,
                    ct)
                ?? topup;
            return await BuildTopupStatusResponseAsync(
                refreshed,
                payosStatus,
                ct);
        }
        catch (OperationCanceledException)
            when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to reconcile booking shortfall top-up {OrderCode}",
                orderCode);
            return await BuildTopupStatusResponseAsync(
                topup,
                topup.Status ?? TopupStatus.Pending,
                ct);
        }
    }

    private async Task<TopupStatusResponse> BuildTopupStatusResponseAsync(
        Topuprequest topup,
        string status,
        CancellationToken ct)
    {
        var wallet = await context.Wallets.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Userid == topup.Userid, ct);
        var walletCredited = topup.Status == TopupStatus.Completed
            && await context.Wallettransactions.AsNoTracking()
                .AnyAsync(t =>
                    t.Ordercode == topup.Ordercode
                    && t.Referencetable == ReferenceTable.Topup,
                    ct);

        return new TopupStatusResponse
        {
            OrderCode = topup.Ordercode,
            BookingId = topup.Bookingid ?? 0,
            PaymentPhase = topup.Paymentphase ?? "",
            Status = status,
            WalletCredited = walletCredited,
            Amount = topup.Amount ?? 0,
            WalletBalance = wallet?.Balance ?? 0
        };
    }
}
