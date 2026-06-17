using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MV.ApplicationLayer.Helpers;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Configuration;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using MV.DomainLayer.Helpers;
using MV.ApplicationLayer.Interfaces;
using PayOS;

namespace MV.ApplicationLayer.Services;

public class WalletService(
    IAppDbContext context,
    IOptions<PaymentSettings> paymentSettings,
    [FromKeyedServices(ServiceKeys.PayOS.Checkout)] PayOSClient payOS,
    ILogger<WalletService> logger) : IWalletService
{
    private readonly PayOSClient _payOS = payOS;
    private readonly PayOSLinkFactory _linkFactory = new(
        payOS,
        paymentSettings.Value.ReturnUrl,
        paymentSettings.Value.CancelUrl);

    public async Task<TopupResponse> CreateTopupRequestAsync(string userId, TopupRequest request)
    {
        var orderCode = await GenerateUniqueOrderCodeAsync(userId);
        var topup = new Topuprequest
        {
            Ordercode = orderCode,
            Userid = userId,
            Amount = request.Amount,
            Status = TopupStatus.Pending,
            Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow,
            Expiresat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow.AddHours(24)
        };

        context.Topuprequests.Add(topup);

        try
        {
            var paymentLink = await _linkFactory.CreatePaymentLink(orderCode, (int)request.Amount, $"Topup #{orderCode}", (int)DateTimeOffset.UtcNow.AddHours(24).ToUnixTimeSeconds());
            topup.Paymentlinkid = paymentLink.PaymentLinkId;
            await context.SaveChangesAsync();

            logger.LogInformation("Created topup request {OrderCode} for user {UserId}", orderCode, userId);

            return new TopupResponse
            {
                PaymentLinkId = paymentLink.PaymentLinkId,
                OrderCode = orderCode,
                Amount = request.Amount,
                Currency = paymentLink.Currency ?? Currency.Vnd,
                CheckoutUrl = paymentLink.CheckoutUrl,
                QrCode = paymentLink.QrCode,
                AccountNumber = paymentLink.AccountNumber ?? "",
                AccountName = paymentLink.AccountName ?? "",
                Bin = paymentLink.Bin ?? "",
                Description = paymentLink.Description ?? "",
                ExpiredAt = paymentLink.ExpiredAt.HasValue ? DateTimeOffset.FromUnixTimeSeconds(paymentLink.ExpiredAt.Value).UtcDateTime : null
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create topup request for user {UserId}", userId);
            throw new BookingException(WalletErrorCodes.InvalidAmount, "Tạo yêu cầu nạp tiền thất bại: " + ex.Message, 500);
        }
    }

    public async Task ProcessTopupWebhookAsync(PaymentWebhookRequest request, CancellationToken ct = default)
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

        if (await context.Wallettransactions.AsNoTracking()
            .AnyAsync(w => w.Ordercode == data.OrderCode, ct))
        {
            logger.LogWarning("Duplicate topup transaction {OrderCode}", data.OrderCode);
            return;
        }

        var topup = await context.Topuprequests
            .FirstOrDefaultAsync(t => t.Ordercode == data.OrderCode, ct)
            ?? throw new BookingException(WalletErrorCodes.TopupNotFound, "Không tìm thấy yêu cầu nạp tiền", 404);

        if (data.Amount != (int)(topup.Amount ?? 0))
            throw new BookingException(WalletErrorCodes.AmountMismatch, "Số tiền không khớp", 409);

        if (topup.Status == TopupStatus.Completed)
        {
            logger.LogWarning("Topup {OrderCode} already completed", data.OrderCode);
            return;
        }

        await using var tx = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        try
        {
            var w = await context.Wallets
            .FromSqlRaw(SqlQueries.LockWalletByUserId, topup.Userid)
                .FirstOrDefaultAsync(ct)
                ?? new Wallet { Userid = topup.Userid, Balance = 0, Frozenbalance = 0, Lastupdated = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow };

            if (w.Walletid == 0) context.Wallets.Add(w);

            w.Balance = (w.Balance ?? 0) + topup.Amount;
            w.Lastupdated = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

            context.Wallettransactions.Add(new Wallettransaction
            {
                Wallet = w,
                Amount = topup.Amount,
                Transactiontype = TransactionType.Deposit,
                Referencetable = ReferenceTable.Topup,
                Referenceid = topup.Topuprequestid,
                Description = data.Reference,
                Ordercode = data.OrderCode,
                Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
            });

            topup.Status = TopupStatus.Completed;
            topup.Completedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

            await context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            logger.LogInformation("Topup {OrderCode} completed for user {UserId}", data.OrderCode, topup.Userid);
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
            FrozenBalance = frz,
            TotalBalance = bal + frz,
            LastUpdated = w != null && w.Lastupdated
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

    public async Task<bool> VerifyWebhookSignatureAsync(string payload, string signature)
    {
        try
        {
            var webhook = System.Text.Json.JsonSerializer.Deserialize<PayOS.Models.Webhooks.Webhook>(payload);
            if (webhook == null) return false;
            return await _payOS.Webhooks.VerifyAsync(webhook) != null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Webhook signature verification failed");
            return false;
        }
    }

    public async Task<bool> HasSufficientBalanceForVerificationAsync(string userId, decimal verificationCost)
    {
        var wallet = await context.Wallets.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Userid == userId);

        if (wallet == null)
            return false;

        var balance = wallet.Balance ?? 0;
        return balance >= verificationCost;
    }

    public async Task DeductVerificationFeeAsync(string userId, decimal amount, string verificationCode)
    {
        await using var tx = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        try
        {
            var wallet = await context.Wallets
            .FromSqlRaw(SqlQueries.LockWalletByUserId, userId)
                .FirstOrDefaultAsync()
                ?? throw new BookingException(WalletErrorCodes.WalletNotFound, "Không tìm thấy ví", 404);

            var balance = wallet.Balance ?? 0;
            if (balance < amount)
                throw new BookingException(
                    WalletErrorCodes.InsufficientBalanceForVerification,
                    $"Insufficient balance. Required: {amount}, Available: {balance}",
                    400);

            wallet.Balance = balance - amount;
            wallet.Lastupdated = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

            context.Wallettransactions.Add(new Wallettransaction
            {
                Wallet = wallet,
                Amount = -amount,
                Transactiontype = TransactionType.BankVerification,
                Referencetable = ReferenceTable.TutorProfiles,
                Referenceid = null,
                Description = $"Bank verification fee - Code: {verificationCode}",
                Ordercode = null,
                Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
            });

            await context.SaveChangesAsync();
            await tx.CommitAsync();

            logger.LogInformation("Deducted {Amount} verification fee for user {UserId}", amount, userId);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    private async Task<long> GenerateUniqueOrderCodeAsync(string userId)
    {
        for (var i = 0; i < 10; i++)
        {
            var orderCode = OrderCodeHelper.GenerateTopupOrderCode(userId);

            if (!await context.Topuprequests.AsNoTracking()
                .AnyAsync(t => t.Ordercode == orderCode))
                return orderCode;
        }

        throw new BookingException(WalletErrorCodes.InvalidAmount, "Không thể tạo mã đơn hàng duy nhất", 500);
    }
}
