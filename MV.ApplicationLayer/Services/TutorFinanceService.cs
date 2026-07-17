using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
    ILogger<TutorFinanceService> logger) : ITutorFinanceService
{
    private const decimal MinWithdrawalAmount = 100000m;

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

        var balance = wallet.Balance ?? 0;
        var frozenBalance = wallet.Frozenbalance ?? 0;

        return new FinanceSummaryResponse
        {
            AvailableBalance = balance,
            FrozenBalance = frozenBalance,
            TotalBalance = balance + frozenBalance,
            TotalEarned = totalEarned,
            PendingSettlement = pendingSettlement,
            LastWithdrawalAt = lastWithdrawal
        };
    }

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
            query = query.Where(t => t.Transactiontype == type);

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

        return new TransactionHistoryResponse
        {
            TransactionId = raw.Transactionid,
            Amount = raw.Amount ?? 0,
            TransactionType = raw.Transactiontype ?? string.Empty,
            Description = raw.Description ?? string.Empty,
            ReferenceId = raw.Referenceid,
            ReferenceTable = raw.Referencetable,
            CreatedAt = raw.Createdat ?? MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
        };
    }

    public async Task<TutorBankInfoResponse> GetBankInfoAsync(string tutorId, CancellationToken ct = default)
    {
        var tutorExists = await context.Tutorprofiles
            .AsNoTracking()
            .AnyAsync(t => t.Tutorid == tutorId, ct);

        if (!tutorExists)
            throw new TutorProfileNotFoundException();

        var bankAccount = await context.BankAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Userid == tutorId, ct);

        return new TutorBankInfoResponse
        {
            BankName = bankAccount?.Bankname,
            AccountNumber = bankAccount?.Accountnumber,
            AccountHolderName = bankAccount?.Accountholdername,
            BankChangedAt = bankAccount?.Updatedat
        };
    }
    public async Task<TutorBankInfoResponse> UpdateBankInfoAsync(string tutorId, UpdateTutorBankInfoRequest request, CancellationToken ct = default)
    {
        var tutor = await context.Tutorprofiles.FirstOrDefaultAsync(t => t.Tutorid == tutorId, ct);
        if (tutor == null)
            throw new TutorProfileNotFoundException();

        var now = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
        var bankAccount = await context.BankAccounts.FirstOrDefaultAsync(b => b.Userid == tutorId, ct);

        if (bankAccount == null)
        {
            bankAccount = new BankAccount
            {
                Userid = tutorId,
                Createdat = now
            };
            context.BankAccounts.Add(bankAccount);
        }

        bankAccount.Bankname = request.BankName;
        bankAccount.Accountnumber = request.AccountNumber;
        bankAccount.Accountholdername = request.AccountHolderName;
        bankAccount.Updatedat = now;
        tutor.Updatedat = now;

        await context.SaveChangesAsync(ct);

        logger.LogInformation("Updated bank info for tutor {TutorId}", tutorId);

        return new TutorBankInfoResponse
        {
            BankName = bankAccount.Bankname,
            AccountNumber = bankAccount.Accountnumber,
            AccountHolderName = bankAccount.Accountholdername,
            BankChangedAt = bankAccount.Updatedat
        };
    }
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

            if (request.Amount < MinWithdrawalAmount)
                throw new WithdrawalAmountTooLowException(MinWithdrawalAmount);

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
                Description = "Withdrawal request",
                Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
            };

            context.Wallettransactions.Add(walletTransaction);

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
                    Message = notificationMessage
                });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to send notification for withdrawal {WithdrawalId}", withdrawal.Withdrawalid);
            }

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
            .Select(w => new { w.Withdrawalid, w.Amount, w.Status, w.Bankname, w.Accountnumber, w.Accountholdername, w.Requestedat, w.Processedat })
            .FirstOrDefaultAsync(ct);

        if (raw == null)
            throw new WithdrawalNotFoundException();

        return new WithdrawalDetailResponse
        {
            WithdrawalId = raw.Withdrawalid,
            Amount = raw.Amount ?? 0,
            Status = raw.Status ?? string.Empty,
            BankName = raw.Bankname,
            AccountNumber = raw.Accountnumber,
            AccountHolderName = raw.Accountholdername,
            RequestedAt = raw.Requestedat ?? MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow,
            ProcessedAt = raw.Processedat
        };
    }

    public async Task CancelWithdrawalAsync(string tutorId, int withdrawalId, CancellationToken ct = default)
    {
        var withdrawal = await context.Withdrawalrequests
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Withdrawalid == withdrawalId && w.Userid == tutorId, ct);

        if (withdrawal == null)
            throw new WithdrawalNotFoundException();

        // Manual bank transfer has no system-side "in progress" lock. Staff must reject the
        // request after verifying no transfer was sent; tutor self-cancel would risk double payout.
        throw new WithdrawalCancellationException();
    }

    private static int GetWeekOfYear(DateTime date)
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        return culture.Calendar.GetWeekOfYear(date, System.Globalization.CalendarWeekRule.FirstDay, DayOfWeek.Monday);
    }
}
