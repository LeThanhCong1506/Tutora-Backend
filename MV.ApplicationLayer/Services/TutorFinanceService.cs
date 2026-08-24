using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Helpers;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using MV.DomainLayer.Helpers;
using MV.ApplicationLayer.Interfaces;
using static MV.DomainLayer.Constants.ClassSessionStatus;

namespace MV.ApplicationLayer.Services;

public class TutorFinanceService(
    IAppDbContext context,
    INotificationService notificationService,
    IFileStorageService fileStorageService,
    IWithdrawalLimitService withdrawalLimitService,
    ILogger<TutorFinanceService> logger) : ITutorFinanceService
{

    public async Task<FinanceSummaryResponse> GetSummaryAsync(string tutorId, CancellationToken ct = default)
    {
        var wallet = await context.Wallets
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Userid == tutorId, ct);

        if (wallet == null)
            throw new WalletNotFoundException();

        var totalEarned = await context.Wallettransactions
            .AsNoTracking()
            .Where(t => t.Wallet!.Userid == tutorId && t.Transactiontype == TransactionType.EscrowRelease)
            .SumAsync(t => t.Amount ?? 0, ct);

        var pendingSettlement = await context.ClassSessions
            .AsNoTracking()
            .Where(l => l.Tutorid == tutorId && l.Issettled == false && l.Status == Completed)
            .SumAsync(l => l.Lessonprice ?? 0, ct);

        var lastWithdrawal = await context.Withdrawalrequests
            .AsNoTracking()
            .Where(w => w.Userid == tutorId)
            .OrderByDescending(w => w.Requestedat)
            .Select(w => w.Requestedat)
            .FirstOrDefaultAsync(ct);

        var hasActiveDispute = await HasActiveDisputeAsync(tutorId, ct);

        var disputedAmount = await context.ClassSessions
            .AsNoTracking()
            .Where(l => l.Tutorid == tutorId
                        && l.Disputes.Any(d => d.Status != DisputeStatus.Resolved && d.Status != DisputeStatus.Closed))
            .SumAsync(l => l.Lessonprice ?? 0, ct);

        var balance = wallet.Balance ?? 0;
        var frozenBalance = wallet.Frozenbalance ?? 0;

        return new FinanceSummaryResponse
        {
            AvailableBalance = balance,
            FrozenBalance = frozenBalance,
            TotalBalance = balance + frozenBalance,
            TotalEarned = totalEarned,
            PendingSettlement = pendingSettlement,
            LastWithdrawalAt = lastWithdrawal,
            HasActiveDispute = hasActiveDispute,
            DisputedAmount = disputedAmount
        };
    }

    private Task<bool> HasActiveDisputeAsync(string tutorId, CancellationToken ct) =>
        context.Disputes
            .AsNoTracking()
            .AnyAsync(d => d.ClassSession != null && d.ClassSession.Tutorid == tutorId
                           && d.Status != DisputeStatus.Resolved && d.Status != DisputeStatus.Closed, ct);

    public async Task<EarningsResponse> GetEarningsAsync(string tutorId, string period, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        from ??= MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow.AddMonths(-6);
        to ??= MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

        var transactions = await context.Wallettransactions
            .AsNoTracking()
            .Where(t => t.Wallet!.Userid == tutorId
                        && t.Transactiontype == TransactionType.EscrowRelease
                        && t.Createdat >= from
                        && t.Createdat <= to)
            .Select(t => new { t.Amount, t.Createdat })
            .ToListAsync(ct);

        var groupedData = period.ToLower() switch
        {
            EarningsPeriod.Week => transactions
                .GroupBy(t => new { Year = t.Createdat!.Value.Year, Week = GetWeekOfYear(t.Createdat.Value) })
                .Select(g => new EarningsItem
                {
                    Date = $"{g.Key.Year}-W{g.Key.Week:D2}",
                    Amount = g.Sum(x => x.Amount ?? 0)
                })
                .OrderBy(x => x.Date)
                .ToList(),
            EarningsPeriod.Year => transactions
                .GroupBy(t => t.Createdat!.Value.Year)
                .Select(g => new EarningsItem
                {
                    Date = g.Key.ToString(),
                    Amount = g.Sum(x => x.Amount ?? 0)
                })
                .OrderBy(x => x.Date)
                .ToList(),
            _ => transactions
                .GroupBy(t => new { t.Createdat!.Value.Year, t.Createdat.Value.Month })
                .Select(g => new EarningsItem
                {
                    Date = $"{g.Key.Year}-{g.Key.Month:D2}",
                    Amount = g.Sum(x => x.Amount ?? 0)
                })
                .OrderBy(x => x.Date)
                .ToList()
        };

        return new EarningsResponse { Items = groupedData };
    }

    public async Task<TransactionHistoryPagedResponse> GetTransactionsAsync(
        string tutorId, int page, int pageSize, string? type, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var query = context.Wallettransactions
            .AsNoTracking()
            .Where(t => t.Wallet!.Userid == tutorId);

        if (!string.IsNullOrEmpty(type))
        {
            // Người dùng chủ động lọc theo loại cụ thể (kể cả EscrowCredit/EscrowReversal) —
            // tôn trọng lựa chọn đó, không áp exclusion mặc định bên dưới.
            query = query.Where(t => t.Transactiontype == type);
        }
        else
        {
            // Mặc định (không chọn loại): EscrowCredit/EscrowReversal chỉ đụng tới Frozenbalance
            // (tiền đang giữ, gia sư chưa thực nhận) — không ảnh hưởng Balance (số dư khả dụng
            // thật). Hiện mặc định sẽ gây loãng thông tin vì gia sư không "nhận" hay "mất" gì qua
            // 2 loại này; số dư đang giữ đã có riêng ở tab "Tổng quan". Chỉ EscrowRelease (giải
            // ngân thật) mới thuộc lịch sử giao dịch ví mặc định.
            query = query.Where(t => t.Transactiontype != TransactionType.EscrowCredit
                && t.Transactiontype != TransactionType.EscrowReversal);
        }

        if (from.HasValue)
        {
            var fromUtc = from.Value.Kind == DateTimeKind.Utc
                ? from.Value
                : DateTime.SpecifyKind(from.Value, DateTimeKind.Utc);
            query = query.Where(t => t.Createdat >= fromUtc);
        }
        if (to.HasValue)
        {
            var toUtc = to.Value.Kind == DateTimeKind.Utc
                ? to.Value
                : DateTime.SpecifyKind(to.Value, DateTimeKind.Utc);
            query = query.Where(t => t.Createdat <= toUtc);
        }

        var total = await query.CountAsync(ct);

        var rawItems = await query
            .OrderByDescending(t => t.Createdat)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new { t.Transactionid, t.Amount, t.Transactiontype, t.Description, t.Createdat })
            .ToListAsync(ct);

        var items = rawItems.Select(t => new TransactionHistoryResponse
        {
            TransactionId = t.Transactionid,
            Amount = t.Amount ?? 0,
            TransactionType = t.Transactiontype ?? string.Empty,
            Description = t.Description ?? string.Empty,
            CreatedAt = t.Createdat ?? MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
        }).ToList();

        return new TransactionHistoryPagedResponse
        {
            Transactions = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<TransactionHistoryResponse> GetTransactionDetailAsync(string tutorId, int transactionId, CancellationToken ct = default)
    {
        var raw = await context.Wallettransactions
            .AsNoTracking()
            .Where(t => t.Transactionid == transactionId && t.Wallet!.Userid == tutorId)
            .Select(t => new { t.Transactionid, t.Amount, t.Transactiontype, t.Description, t.Referenceid, t.Referencetable, t.Createdat })
            .FirstOrDefaultAsync(ct);

        if (raw == null)
            throw new TransactionNotFoundException();

        string? providerTransactionId = null;
        DateTime? paidAt = null;
        string? proofImageUrl = null;

        if (raw.Referencetable == ReferenceTable.Withdrawal && raw.Referenceid.HasValue)
        {
            var proof = await MV.ApplicationLayer.Helpers.PayoutProofResolver.ResolveAsync(
                context, fileStorageService, raw.Referenceid.Value, ct);
            providerTransactionId = proof.ProviderTransactionId;
            paidAt = proof.PaidAt;
            proofImageUrl = proof.ProofImageUrl;
        }

        return new TransactionHistoryResponse
        {
            TransactionId = raw.Transactionid,
            Amount = raw.Amount ?? 0,
            TransactionType = raw.Transactiontype ?? string.Empty,
            Description = raw.Description ?? string.Empty,
            ReferenceId = raw.Referenceid,
            ReferenceTable = raw.Referencetable,
            CreatedAt = raw.Createdat ?? MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow,
            ProviderTransactionId = providerTransactionId,
            PaidAt = paidAt,
            ProofImageUrl = proofImageUrl
        };
    }

    // Bank account CRUD (GetBankInfoAsync/UpdateBankInfoAsync/DeleteBankInfoAsync) moved to the
    // shared BankAccountService/BankAccountController (api/bank-account) — Parent/Student now
    // need the same feature, and it now requires OTP verification on every write.

    public async Task<WithdrawalDetailResponse> CreateWithdrawalAsync(string tutorId, CreateWithdrawalRequest request, CancellationToken ct = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var committed = false;

        try
        {
            var wallet = await context.Wallets
            .FromSqlRaw(SqlQueries.LockWalletByUserId, tutorId)
                .FirstOrDefaultAsync(ct);

            if (wallet == null)
                throw new WalletNotFoundException();

            if (await HasActiveDisputeAsync(tutorId, ct))
                throw new ActiveDisputeException();

            if ((wallet.Balance ?? 0) < request.Amount)
                throw new InsufficientBalanceException();

            var pendingWithdrawal = await context.Withdrawalrequests
                .AnyAsync(w => w.Userid == tutorId
                               && (w.Status == WithdrawalStatus.Pending
                                   || w.Status == WithdrawalStatus.Delayed
                                   || w.Status == WithdrawalStatus.PendingReview
                                   || w.Status == WithdrawalStatus.Approved), ct);

            if (pendingWithdrawal)
                throw new PendingWithdrawalException();

            var tutorExists = await context.Tutorprofiles
                .AsNoTracking()
                .AnyAsync(t => t.Tutorid == tutorId, ct);
            var bankAccount = await context.BankAccounts
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.Userid == tutorId, ct);

            if (!tutorExists
                || bankAccount == null
                || string.IsNullOrEmpty(bankAccount.Bankname)
                || string.IsNullOrEmpty(bankAccount.Accountnumber)
                || string.IsNullOrEmpty(bankAccount.Accountholdername))
                throw new BankInfoRequiredException();

            var minWithdrawalAmount = await withdrawalLimitService.GetMinWithdrawalAmountAsync(ct);
            if (request.Amount < minWithdrawalAmount)
                throw new WithdrawalAmountTooLowException(minWithdrawalAmount);

            wallet.Balance -= request.Amount;
            wallet.Lastupdated = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

            var withdrawal = new Withdrawalrequest
            {
                Userid = tutorId,
                Walletid = wallet.Walletid,
                Amount = request.Amount,
                Bankname = bankAccount.Bankname,
                Accountnumber = bankAccount.Accountnumber,
                Accountholdername = bankAccount.Accountholdername,
                Status = WithdrawalStatus.PendingReview,
                Decision = TrustScoringConstants.Decisions.ManualReview,
                Requestedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
            };

            context.Withdrawalrequests.Add(withdrawal);

            var walletTransaction = new Wallettransaction
            {
                Walletid = wallet.Walletid,
                Amount = -request.Amount,
                Transactiontype = TransactionType.Withdrawal,
                Referencetable = ReferenceTable.Withdrawal,
                Description = "Withdrawal request",
                Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
            };

            context.Wallettransactions.Add(walletTransaction);

            await context.SaveChangesAsync(ct);

            walletTransaction.Referenceid = withdrawal.Withdrawalid;
            walletTransaction.Description = $"Withdrawal request #{withdrawal.Withdrawalid}";
            await context.SaveChangesAsync(ct);

            await transaction.CommitAsync(ct);
            committed = true;

            const string notificationMessage = "Yêu cầu rút tiền của bạn đã được ghi nhận và đang chờ admin/staff xét duyệt, dự kiến trong vòng 24 giờ.";

            try
            {
                await notificationService.CreateNotificationAsync(new NotificationRequest
                {
                    Userid = tutorId,
                    Title = "Yêu cầu rút tiền đã được tạo",
                    Message = notificationMessage,
                    Type = NotificationType.WithdrawalRequest,
                    Referenceid = withdrawal.Withdrawalid.ToString()
                });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to send notification for withdrawal {WithdrawalId}", withdrawal.Withdrawalid);
            }

            // Người duyệt (Admin + Staff có payout.view) cần biết ngay; đặt sau notification của người
            // yêu cầu vì cả hai đều best-effort, lỗi bên này không được chặn bên kia.
            await WithdrawalReviewerNotifier.NotifyNewRequestAsync(
                context, notificationService, logger, withdrawal, ct);

            logger.LogInformation("Created withdrawal {WithdrawalId} for tutor {TutorId}, amount: {Amount}",
                withdrawal.Withdrawalid, tutorId, request.Amount);

            return new WithdrawalDetailResponse
            {
                WithdrawalId = withdrawal.Withdrawalid,
                Amount = withdrawal.Amount ?? 0,
                Status = withdrawal.Status ?? string.Empty,
                BankName = withdrawal.Bankname,
                AccountNumber = withdrawal.Accountnumber,
                AccountHolderName = withdrawal.Accountholdername,
                RequestedAt = withdrawal.Requestedat ?? MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow,
                ProcessedAt = withdrawal.Processedat
            };
        }
        catch
        {
            // Only roll back if we have NOT committed yet; rolling back a committed tx throws.
            if (!committed)
                await transaction.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<WithdrawalListResponse> GetWithdrawalsAsync(string tutorId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = context.Withdrawalrequests
            .AsNoTracking()
            .Where(w => w.Userid == tutorId);

        var total = await query.CountAsync(ct);

        var rawItems = await query
            .OrderByDescending(w => w.Requestedat)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(w => new { w.Withdrawalid, w.Amount, w.Status, w.Requestedat, w.Processedat })
            .ToListAsync(ct);

        var items = rawItems.Select(w => new WithdrawalItem
        {
            WithdrawalId = w.Withdrawalid,
            Amount = w.Amount ?? 0,
            Status = w.Status ?? string.Empty,
            RequestedAt = w.Requestedat ?? MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow,
            ProcessedAt = w.Processedat
        }).ToList();

        return new WithdrawalListResponse
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<WithdrawalDetailResponse> GetWithdrawalDetailAsync(string tutorId, int withdrawalId, CancellationToken ct = default)
    {
        var raw = await context.Withdrawalrequests
            .AsNoTracking()
            .Where(w => w.Withdrawalid == withdrawalId && w.Userid == tutorId)
            .Select(w => new
            {
                w.Withdrawalid,
                w.Amount,
                w.Status,
                w.Bankname,
                w.Accountnumber,
                w.Accountholdername,
                w.Requestedat,
                w.Processedat,
                w.Claimedat,
                w.Completionnote,
                w.Rejectionreason
            })
            .FirstOrDefaultAsync(ct);

        if (raw == null)
            throw new WithdrawalNotFoundException();

        var proof = await MV.ApplicationLayer.Helpers.PayoutProofResolver.ResolveAsync(
            context, fileStorageService, withdrawalId, ct);

        return new WithdrawalDetailResponse
        {
            WithdrawalId = raw.Withdrawalid,
            Amount = raw.Amount ?? 0,
            Status = raw.Status ?? string.Empty,
            BankName = raw.Bankname,
            AccountNumber = raw.Accountnumber,
            AccountHolderName = raw.Accountholdername,
            RequestedAt = raw.Requestedat ?? MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow,
            ProcessedAt = raw.Processedat,
            ClaimedAt = raw.Claimedat,
            CompletionNote = raw.Completionnote,
            RejectionReason = raw.Rejectionreason,
            TransactionId = proof.ProviderTransactionId,
            BankTransactionCode = proof.BankTransactionCode,
            PaidAt = proof.PaidAt,
            ProofImageUrl = proof.ProofImageUrl
        };
    }

    public async Task CancelWithdrawalAsync(string tutorId, int withdrawalId, CancellationToken ct = default)
    {
        var withdrawal = await context.Withdrawalrequests
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Withdrawalid == withdrawalId && w.Userid == tutorId, ct);

        if (withdrawal == null)
            throw new WithdrawalNotFoundException();

        // A claimed request may already be in external bank processing. Staff must reject it
        // after verifying no transfer was sent; tutor self-cancel would risk a double payout.
        throw new WithdrawalCancellationException();
    }

    private static int GetWeekOfYear(DateTime date)
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        return culture.Calendar.GetWeekOfYear(date, System.Globalization.CalendarWeekRule.FirstDay, DayOfWeek.Monday);
    }
}
