using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MV.ApplicationLayer.Helpers;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Configuration;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using MV.DomainLayer.Helpers;
using PayOS;

namespace MV.ApplicationLayer.Services;

public class AiCreditService(
    IAppDbContext context,
    IOptions<PaymentSettings> paymentSettings,
    [FromKeyedServices(ServiceKeys.PayOS.Checkout)] PayOSClient payOS,
    IFileStorageService fileStorage,
    ILogger<AiCreditService> logger) : IAiCreditService
{
    private readonly PayOSLinkFactory _linkFactory = new(
        payOS,
        paymentSettings.Value.ReturnUrl,
        paymentSettings.Value.CancelUrl);

    // Ledger core
    public async Task<int> GrantAsync(
        string userId, int amount, string source, string? referenceId, string? description,
        CancellationToken ct = default)
    {
        if (amount <= 0)
            throw new BookingException(AiCreditErrorCodes.InvalidAmount, "Số credit cấp phải > 0.", 400);

        return await ApplyDeltaAsync(userId, amount, source, referenceId, description, ct);
    }

    /// <summary>
    /// Tiêu credit ( hỏi bài). THIẾT KẾ: chỉ giảm counter <c>users.ai_credits_balance</c>
    /// Trừ atomic bằng 1 câu UPDATE có điều kiện
    /// <c>WHERE balance &gt;= amount</c> → không race, không read-modify-write, đủ credit mới trừ.
    /// </summary>
    public async Task<int> SpendAsync(
        string userId, int amount, string? referenceId, string? description,
        CancellationToken ct = default)
    {
        if (amount <= 0)
            throw new BookingException(AiCreditErrorCodes.InvalidAmount, "Số credit tiêu phải > 0.", 400);

        var affected = await context.Users
            .Where(u => u.Userid == userId && u.AiCreditsBalance >= amount)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.AiCreditsBalance, u => u.AiCreditsBalance - amount), ct);

        if (affected == 0)
        {
            // Không trừ được: hoặc không đủ credit, hoặc user không tồn tại.
            var exists = await context.Users.AsNoTracking().AnyAsync(u => u.Userid == userId, ct);
            if (!exists)
                throw new BookingException(AiCreditErrorCodes.UserNotFound, "Không tìm thấy tài khoản.", 404);
            throw new BookingException(AiCreditErrorCodes.InsufficientCredits, "Số lượt sử dụng đã hết.", 400);
        }

        return await context.Users.AsNoTracking()
            .Where(u => u.Userid == userId).Select(u => u.AiCreditsBalance).FirstAsync(ct);
    }

    /// <summary>Áp một delta (dương = cấp, âm = tiêu) lên số dư của tài khoản trong 1 transaction serializable:
    /// khóa user, kiểm tra đủ credit (nếu âm), ghi ledger, cập nhật cache balance.</summary>
    private async Task<int> ApplyDeltaAsync(
        string userId, int delta, string source, string? referenceId, string? description,
        CancellationToken ct)
    {
        await using var tx = await context.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, ct);
        try
        {
            var user = await context.Users
                .FirstOrDefaultAsync(u => u.Userid == userId, ct)
                ?? throw new BookingException(AiCreditErrorCodes.UserNotFound, "Không tìm thấy tài khoản.", 404);

            var newBalance = user.AiCreditsBalance + delta;
            if (newBalance < 0)
                throw new BookingException(
                    AiCreditErrorCodes.InsufficientCredits,
                    "Số lượt sử dụng đã hết.", 400);

            user.AiCreditsBalance = newBalance;

            context.AiCreditTransactions.Add(new AiCreditTransaction
            {
                Userid = userId,
                Amount = delta,
                Balanceafter = newBalance,
                Source = source,
                Referenceid = referenceId,
                Description = description,
                Createdat = TimeZoneHelper.UtcNow
            });

            await context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return newBalance;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task GrantFreePackageAsync(string userId, CancellationToken ct = default)
    {
        var freePkg = await context.AiCreditPackages
            .FirstOrDefaultAsync(p => p.Code == AiCreditPackageCode.Free && p.Isactive, ct);
        if (freePkg is null || freePkg.Creditamount <= 0)
        {
            logger.LogWarning("Free AI credit package missing/inactive; skipping grant for user {UserId}", userId);
            return;
        }

        var refId = $"free:{userId}";
        if (await ReferenceAlreadyGrantedAsync(userId, AiCreditSource.Grant, refId, ct))
            return;

        await GrantAsync(userId, freePkg.Creditamount, AiCreditSource.Grant, refId,
            "Tặng gói Free khi tạo tài khoản.", ct);
    }

    public async Task GrantBookingBonusAsync(string userId, int bookingId, CancellationToken ct = default)
    {
        var bonus = await AdminGetBookingBonusAsync(ct);
        if (bonus <= 0) return;

        var refId = $"booking:{bookingId}";
        if (await ReferenceAlreadyGrantedAsync(userId, AiCreditSource.Grant, refId, ct))
            return;

        await GrantAsync(userId, bonus, AiCreditSource.Grant, refId,
            $"Tặng credit khi có booking #{bookingId}.", ct);
    }

    private Task<bool> ReferenceAlreadyGrantedAsync(string userId, string source, string referenceId, CancellationToken ct)
        => context.AiCreditTransactions.AnyAsync(
            t => t.Userid == userId && t.Source == source && t.Referenceid == referenceId, ct);

    // Query

    public async Task<AiCreditBalanceResponse> GetBalanceAsync(string userId, CancellationToken ct = default)
    {
        var user = await context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Userid == userId, ct)
            ?? throw new BookingException(AiCreditErrorCodes.UserNotFound, "Không tìm thấy tài khoản.", 404);

        return new AiCreditBalanceResponse { Balance = user.AiCreditsBalance };
    }

    public async Task<IReadOnlyList<AiCreditTransactionResponse>> GetHistoryAsync(
        string userId, int take, CancellationToken ct = default)
    {
        take = take is <= 0 or > 200 ? 50 : take;
        return await context.AiCreditTransactions
            .AsNoTracking()
            .Where(t => t.Userid == userId)
            .OrderByDescending(t => t.Createdat)
            .Take(take)
            .Select(t => new AiCreditTransactionResponse
            {
                TransactionId = t.Transactionid,
                Amount = t.Amount,
                BalanceAfter = t.Balanceafter,
                Source = t.Source,
                ReferenceId = t.Referenceid,
                Description = t.Description,
                CreatedAt = t.Createdat
            })
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AiCreditPackageResponse>> GetActivePackagesAsync(CancellationToken ct = default)
        => await context.AiCreditPackages
            .AsNoTracking()
            .Where(p => p.Isactive)
            .OrderBy(p => p.Sortorder).ThenBy(p => p.Packageid)
            .Select(p => ToPackageResponse(p))
            .ToListAsync(ct);

    // Purchase

    public async Task<AiCreditPurchaseResponse> InitiatePurchaseAsync(
        string buyerUserId, AiCreditPurchaseRequest request, CancellationToken ct = default)
    {
        var package = await context.AiCreditPackages
            .FirstOrDefaultAsync(p => p.Packageid == request.PackageId, ct)
            ?? throw new BookingException(AiCreditErrorCodes.PackageNotFound, "Không tìm thấy gói.", 404);

        if (!package.Isactive || !package.Ispurchasable)
            throw new BookingException(AiCreditErrorCodes.PackageNotPurchasable, "Gói này không mở bán.", 400);

        // App độc lập: người đăng nhập mua cho CHÍNH tài khoản của họ (mọi role).
        // Không cần validate quyền trên student — credit về đúng người login.
        var orderCode = await GenerateUniqueOrderCodeAsync(ct);
        var now = TimeZoneHelper.UtcNow;
        var amount = checked((int)package.Price);

        // Ý định thanh toán PENDING → payment_requests (không ghi payment_transactions
        // cho tới khi webhook báo thành công — payment_transactions là sổ cái success-only).
        var paymentRequest = new PaymentRequest
        {
            Bookingid = null,                              
            Userid = buyerUserId,                          
            Provider = PaymentRequestProvider.PayOS,
            Phase = PaymentRequestPhase.AiCredit,
            Ordercode = orderCode,
            Amount = package.Price,
            Currency = Currency.Vnd,
            Status = PaymentRequestStatus.Pending,
            AiCreditPackageid = package.Packageid,
            AiCreditUserid = buyerUserId,                  
            Description = $"Thanh Toan Tutora AI Goi {package.Name}",
            Expiresat = now.AddHours(24),
            Createdat = now,
            Updatedat = now
        };
        context.PaymentRequests.Add(paymentRequest);
        await context.SaveChangesAsync(ct);

        var expiredAtUnix = (int)new DateTimeOffset(
            DateTime.SpecifyKind(paymentRequest.Expiresat!.Value, DateTimeKind.Utc)).ToUnixTimeSeconds();

        var link = await _linkFactory.CreatePaymentLink(
            orderCode, amount, $"Thanh Toan Tutora AI Goi {package.Code}", expiredAtUnix);

        paymentRequest.Paymentlinkid = link.PaymentLinkId;
        paymentRequest.Checkouturl = link.CheckoutUrl;
        paymentRequest.Qrcode = link.QrCode;
        paymentRequest.Updatedat = TimeZoneHelper.UtcNow;
        await context.SaveChangesAsync(ct);

        logger.LogInformation(
            "Initiated AI credit purchase order {OrderCode} package {Package} buyer {Buyer} amount {Amount}",
            orderCode, package.Code, buyerUserId, package.Price);

        return new AiCreditPurchaseResponse
        {
            OrderCode = orderCode,
            CheckoutUrl = link.CheckoutUrl,
            QrCode = link.QrCode,
            PaymentLinkId = link.PaymentLinkId,
            Amount = package.Price,
            PackageId = package.Packageid
        };
    }

    public async Task CompletePurchaseAsync(PaymentWebhookRequest webhook, string? rawPayload, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(webhook);
        ArgumentNullException.ThrowIfNull(webhook.Data);
        var orderCode = webhook.Data.OrderCode;

        var pr = await context.PaymentRequests
            .FirstOrDefaultAsync(r =>
                r.Ordercode == orderCode &&
                r.Phase == PaymentRequestPhase.AiCredit, ct);
        if (pr is null)
        {
            logger.LogWarning("AI credit purchase webhook for unknown order {OrderCode}", orderCode);
            return;
        }

        // Idempotent: đã ghi credit rồi thì thôi (chống webhook lặp).
        var alreadyCredited = await context.AiCreditTransactions
            .AnyAsync(t => t.Source == AiCreditSource.Purchase && t.Referenceid == orderCode.ToString(), ct);
        if (alreadyCredited)
        {
            logger.LogInformation("AI credit order {OrderCode} already credited; skipping.", orderCode);
            return;
        }

        if (pr.AiCreditPackageid is null || string.IsNullOrWhiteSpace(pr.AiCreditUserid))
        {
            logger.LogError("AI credit order {OrderCode} thiếu package/user; không thể cộng credit.", orderCode);
            return;
        }

        var package = await context.AiCreditPackages
            .FirstOrDefaultAsync(p => p.Packageid == pr.AiCreditPackageid, ct);
        if (package is null)
        {
            logger.LogError("AI credit package {PackageId} not found for order {OrderCode}.", pr.AiCreditPackageid, orderCode);
            return;
        }

        var beneficiaryUserId = pr.AiCreditUserid;

        // Ghi sổ cái giao dịch thành công qua factory chuẩn (giống luồng booking) —
        // capture đầy đủ provider tx id, paid-at, tài khoản nguồn, payload để đối soát.
        var capture = PaymentTransactionCapture.FromPayOSWebhook(webhook, rawPayload);
        var pt = capture.Create(
            purpose: PaymentTransactionPurpose.AiCreditPurchase,
            direction: PaymentTransactionDirection.Inbound,
            amount: pr.Amount ?? package.Price,
            userId: pr.Userid,
            orderCode: orderCode,
            description: $"Goi AI {package.Name}",
            paymentRequestId: pr.Paymentrequestid,
            aiCreditPackageId: package.Packageid,
            aiCreditUserId: beneficiaryUserId);
        context.PaymentTransactions.Add(pt);

        pr.Status = PaymentRequestStatus.Paid;
        pr.Updatedat = TimeZoneHelper.UtcNow;
        await context.SaveChangesAsync(ct);

        await GrantAsync(
            beneficiaryUserId, package.Creditamount, AiCreditSource.Purchase,
            orderCode.ToString(), $"Mua gói {package.Code} (+{package.Creditamount} lượt).", ct);

        logger.LogInformation(
            "Completed AI credit purchase order {OrderCode}: +{Amount} credits to user {UserId}.",
            orderCode, package.Creditamount, beneficiaryUserId);
    }

    private async Task<long> GenerateUniqueOrderCodeAsync(CancellationToken ct)
    {
        for (var i = 0; i < 5; i++)
        {
            var candidate = OrderCodeHelper.GenerateAiCreditOrderCode();
            // Order code phải duy nhất trên cả 2 bảng: intent (payment_requests) + ledger (payment_transactions).
            var exists = await context.PaymentRequests.AnyAsync(r => r.Ordercode == candidate, ct)
                || await context.PaymentTransactions.AnyAsync(t => t.Ordercode == candidate, ct);
            if (!exists) return candidate;
        }
        throw new BookingException("AI_CREDIT_ORDER_CODE", "Không tạo được mã đơn hàng.", 500);
    }

    // Admin CRUD gói
    public async Task<IReadOnlyList<AiCreditPackageResponse>> AdminGetPackagesAsync(CancellationToken ct = default)
        => await context.AiCreditPackages
            .AsNoTracking()
            .OrderBy(p => p.Sortorder).ThenBy(p => p.Packageid)
            .Select(p => ToPackageResponse(p))
            .ToListAsync(ct);

    public async Task<AiCreditPackageResponse> AdminCreatePackageAsync(
        AiCreditPackageCreateRequest request, CancellationToken ct = default)
    {
        var code = request.Code.Trim().ToLowerInvariant();
        var dup = await context.AiCreditPackages.AnyAsync(p => p.Code.ToLower() == code, ct);
        if (dup)
            throw new BookingException(AiCreditErrorCodes.PackageCodeExists, "Mã gói đã tồn tại.", 409);

        var now = TimeZoneHelper.UtcNow;
        var pkg = new AiCreditPackage
        {
            Code = code,
            Name = request.Name.Trim(),
            Creditamount = request.CreditAmount,
            Price = request.Price,
            Currency = string.IsNullOrWhiteSpace(request.Currency) ? "VND" : request.Currency,
            Ispurchasable = request.IsPurchasable,
            Isactive = request.IsActive,
            Sortorder = request.SortOrder,
            Description = request.Description,
            Iconurl = request.IconUrl,
            Createdat = now,
            Updatedat = now
        };
        context.AiCreditPackages.Add(pkg);
        await context.SaveChangesAsync(ct);
        return ToPackageResponse(pkg);
    }

    public async Task<AiCreditPackageResponse> AdminUpdatePackageAsync(
        int packageId, AiCreditPackageUpdateRequest request, CancellationToken ct = default)
    {
        var pkg = await context.AiCreditPackages.FirstOrDefaultAsync(p => p.Packageid == packageId, ct)
            ?? throw new BookingException(AiCreditErrorCodes.PackageNotFound, "Không tìm thấy gói.", 404);

        pkg.Name = request.Name.Trim();
        pkg.Creditamount = request.CreditAmount;
        pkg.Price = request.Price;
        pkg.Currency = string.IsNullOrWhiteSpace(request.Currency) ? "VND" : request.Currency;
        pkg.Ispurchasable = request.IsPurchasable;
        pkg.Isactive = request.IsActive;
        pkg.Sortorder = request.SortOrder;
        pkg.Description = request.Description;
        pkg.Iconurl = request.IconUrl;
        pkg.Updatedat = TimeZoneHelper.UtcNow;

        await context.SaveChangesAsync(ct);
        return ToPackageResponse(pkg);
    }

    public async Task AdminDeletePackageAsync(int packageId, CancellationToken ct = default)
    {
        var pkg = await context.AiCreditPackages.FirstOrDefaultAsync(p => p.Packageid == packageId, ct)
            ?? throw new BookingException(AiCreditErrorCodes.PackageNotFound, "Không tìm thấy gói.", 404);

        // Soft-disable thay vì xóa cứng để không mất lịch sử tham chiếu từ payment_transactions.
        pkg.Isactive = false;
        pkg.Ispurchasable = false;
        pkg.Updatedat = TimeZoneHelper.UtcNow;
        await context.SaveChangesAsync(ct);
    }

    public async Task<AiCreditPackageResponse> AdminUploadIconAsync(
        int packageId, Microsoft.AspNetCore.Http.IFormFile file, CancellationToken ct = default)
    {
        if (file is null || file.Length == 0)
            throw new BookingException(AiCreditErrorCodes.InvalidAmount, "File icon rỗng.", 400);

        var pkg = await context.AiCreditPackages.FirstOrDefaultAsync(p => p.Packageid == packageId, ct)
            ?? throw new BookingException(AiCreditErrorCodes.PackageNotFound, "Không tìm thấy gói.", 404);

        // Xóa icon cũ (best-effort) trước khi thay.
        if (!string.IsNullOrWhiteSpace(pkg.Iconurl))
        {
            try { await fileStorage.DeleteFileAsync(StorageBucket.AiCreditIcons, pkg.Code, pkg.Iconurl); }
            catch (Exception ex) { logger.LogWarning(ex, "Không xóa được icon cũ của gói {PackageId}", packageId); }
        }

        var url = await fileStorage.UploadFileAsync(StorageBucket.AiCreditIcons, pkg.Code, file);
        pkg.Iconurl = url;
        pkg.Updatedat = TimeZoneHelper.UtcNow;
        await context.SaveChangesAsync(ct);

        return ToPackageResponse(pkg);
    }

    // Config bonus

    public async Task<int> AdminGetBookingBonusAsync(CancellationToken ct = default)
    {
        var cfg = await context.Systemconfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Configkey == AiCreditConfigKeys.BonusOnBooking, ct);
        return int.TryParse(cfg?.Configvalue, out var v) ? v : 0;
    }

    public async Task AdminSetBookingBonusAsync(int amount, string? updatedByUserId, CancellationToken ct = default)
    {
        if (amount < 0)
            throw new BookingException(AiCreditErrorCodes.InvalidAmount, "Số bonus không hợp lệ.", 400);

        var cfg = await context.Systemconfigs
            .FirstOrDefaultAsync(c => c.Configkey == AiCreditConfigKeys.BonusOnBooking, ct);
        if (cfg is null)
        {
            cfg = new Systemconfig
            {
                Configkey = AiCreditConfigKeys.BonusOnBooking,
                Description = "Số lượt hỏi AI tặng cho học sinh mỗi khi có booking."
            };
            context.Systemconfigs.Add(cfg);
        }
        cfg.Configvalue = amount.ToString();
        cfg.Updatedby = updatedByUserId;
        cfg.Updatedat = TimeZoneHelper.UtcNow;
        await context.SaveChangesAsync(ct);
    }

    private static AiCreditPackageResponse ToPackageResponse(AiCreditPackage p) => new()
    {
        PackageId = p.Packageid,
        Code = p.Code,
        Name = p.Name,
        CreditAmount = p.Creditamount,
        Price = p.Price,
        Currency = p.Currency,
        IsPurchasable = p.Ispurchasable,
        IsActive = p.Isactive,
        SortOrder = p.Sortorder,
        Description = p.Description,
        IconUrl = p.Iconurl
    };
}
