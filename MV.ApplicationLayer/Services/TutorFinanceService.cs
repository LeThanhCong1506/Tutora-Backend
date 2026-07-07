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

        return new FinanceSummaryResponse
        {
            Balance = wallet.Balance ?? 0,
            FrozenBalance = wallet.Frozenbalance ?? 0,
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
        var tutor = await context.Tutorprofiles
            .AsNoTracking()
            .Where(t => t.Tutorid == tutorId)
            .FirstOrDefaultAsync(ct);

        if (tutor == null)
            throw new TutorProfileNotFoundException();

        return new TutorBankInfoResponse
        {
            BankName = tutor.Bankname,
            AccountNumber = tutor.Bankaccountnumber,
            AccountHolderName = tutor.Bankaccountname,
            IsVerified = tutor.Isbankverified ?? false,
            BankChangedAt = tutor.Bankchangedat
        };
    }

    public async Task<TutorBankInfoResponse> UpdateBankInfoAsync(string tutorId, UpdateTutorBankInfoRequest request, CancellationToken ct = default)
    {
        var tutor = await context.Tutorprofiles.FirstOrDefaultAsync(t => t.Tutorid == tutorId, ct);
        if (tutor == null)
            throw new TutorProfileNotFoundException();

        tutor.Bankname = request.BankName;
        tutor.Bankaccountnumber = request.AccountNumber;
        tutor.Bankaccountname = request.AccountHolderName;
        tutor.Isbankverified = false;
        tutor.Bankchangedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
        tutor.Updatedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

        await context.SaveChangesAsync(ct);

        logger.LogInformation("Updated bank info for tutor {TutorId}", tutorId);

        return new TutorBankInfoResponse
        {
            BankName = tutor.Bankname,
            AccountNumber = tutor.Bankaccountnumber,
            AccountHolderName = tutor.Bankaccountname,
            IsVerified = tutor.Isbankverified ?? false,
            BankChangedAt = tutor.Bankchangedat
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
                                   || w.Status == WithdrawalStatus.PendingReview), ct);

            if (pendingWithdrawal)
                throw new PendingWithdrawalException();

            var tutor = await context.Tutorprofiles
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Tutorid == tutorId, ct);

            if (tutor == null || string.IsNullOrEmpty(tutor.Bankaccountnumber))
                throw new BankInfoRequiredException();

            if (tutor.Isbankverified != true)
                throw new BankNotVerifiedException();

            if (request.Amount < MinWithdrawalAmount)
                throw new WithdrawalAmountTooLowException(MinWithdrawalAmount);

            wallet.Balance -= request.Amount;
            wallet.Lastupdated = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

            var withdrawal = new Withdrawalrequest
            {
                Userid = tutorId,
                Walletid = wallet.Walletid,
                Amount = request.Amount,
                Bankname = tutor.Bankname,
                Accountnumber = tutor.Bankaccountnumber,
                Accountholdername = tutor.Bankaccountname,
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
        await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);

        try
        {
            var withdrawal = await context.Withdrawalrequests
                .FirstOrDefaultAsync(w => w.Withdrawalid == withdrawalId && w.Userid == tutorId, ct);

            if (withdrawal == null)
                throw new WithdrawalNotFoundException();

            if (withdrawal.Status != WithdrawalStatus.Pending && withdrawal.Status != WithdrawalStatus.Delayed)
                throw new WithdrawalCancellationException();

            var wallet = await context.Wallets
            .FromSqlRaw(SqlQueries.LockWalletByUserId, tutorId)
                .FirstOrDefaultAsync(ct);

            if (wallet == null)
                throw new WalletNotFoundException();

            wallet.Balance += withdrawal.Amount ?? 0;
            wallet.Lastupdated = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

            withdrawal.Status = WithdrawalStatus.Cancelled;
            withdrawal.Processedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

            var refundTransaction = new Wallettransaction
            {
                Walletid = wallet.Walletid,
                Amount = withdrawal.Amount ?? 0,
                Transactiontype = TransactionType.Refund,
                Description = $"Cancelled withdrawal #{withdrawalId}",
                Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
            };

            context.Wallettransactions.Add(refundTransaction);

            await context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            logger.LogInformation("Cancelled withdrawal {WithdrawalId} for tutor {TutorId}", withdrawalId, tutorId);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    private static int GetWeekOfYear(DateTime date)
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        return culture.Calendar.GetWeekOfYear(date, System.Globalization.CalendarWeekRule.FirstDay, DayOfWeek.Monday);
    }
}
