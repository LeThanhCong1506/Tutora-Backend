using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.ResponseModel.Admin;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using MV.DomainLayer.Helpers;
using System.Globalization;
using System.Text.Json;

namespace MV.ApplicationLayer.Services;

public class AdminFinancialService(
    IAppDbContext context,
    ILogger<AdminFinancialService> logger) : IAdminFinancialService
{
    // Booking statuses that have generated (or are generating) revenue
    private static readonly string[] RevenueBookingStatuses =
    [
        BookingStatus.Paid,
        BookingStatus.DepositPaid,
        BookingStatus.PendingRemainingPayment,
        BookingStatus.Ongoing,
        BookingStatus.Completed
    ];

    // Booking statuses considered "still active" (money collected, not yet done)
    private static readonly string[] ActiveBookingStatuses =
    [
        BookingStatus.Paid,
        BookingStatus.DepositPaid,
        BookingStatus.PendingRemainingPayment,
        BookingStatus.Ongoing
    ];

    // Withdrawal statuses grouped as "still pending / outstanding"
    private static readonly string[] PendingWithdrawalStatuses =
    [
        WithdrawalStatus.Pending,
        WithdrawalStatus.PendingReview,
        WithdrawalStatus.Delayed,
        WithdrawalStatus.Approved      // approved but payout not yet complete
    ];

    public async Task<AdminFinancialMetricsResponse> GetMetricsAsync(
        DateTime? from,
        DateTime? to,
        string period,
        CancellationToken ct = default)
    {
        // Normalise period
        period = period.Trim().ToLowerInvariant() switch
        {
            "week" => "week",
            "year" => "year",
            _ => "month"
        };

        var nowFallback = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
        var toUtc = to.HasValue
            ? (to.Value.Kind == DateTimeKind.Utc ? to.Value : DateTime.SpecifyKind(to.Value, DateTimeKind.Utc))
            : nowFallback;
        var fromUtc = from.HasValue
            ? (from.Value.Kind == DateTimeKind.Utc ? from.Value : DateTime.SpecifyKind(from.Value, DateTimeKind.Utc))
            : period switch
            {
                "week" => toUtc.AddDays(-7 * 12),
                "year" => toUtc.AddYears(-5),
                _ => toUtc.AddMonths(-12)
            };

        logger.LogInformation(
            "AdminFinancialService.GetMetricsAsync period={Period} from={From} to={To}",
            period, fromUtc, toUtc);

        // Current-month boundaries in UTC (derived from user local time)
        var nowUtc = TimeZoneHelper.UtcNow;
        var userNow = nowUtc;
        var currentMonthStartUser = new DateTime(userNow.Year, userNow.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var currentMonthStartUtc = DateTime.SpecifyKind(currentMonthStartUser, DateTimeKind.Utc);
        var previousMonthStartUtc = currentMonthStartUtc.AddMonths(-1);
        var currentYearStartUser = new DateTime(userNow.Year, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var currentYearStartUtc = DateTime.SpecifyKind(currentYearStartUser, DateTimeKind.Utc);

        // ─── Fetch raw projections in parallel ──────────────────────────────
        var bookingsRawTask = context.Bookings
            .AsNoTracking()
            .Select(b => new BookingRaw(
                b.Status,
                TeachingMode.Online,
                b.Tutorsubjectgradeprice != null ? b.Tutorsubjectgradeprice.Subjectid : null,
                b.Platformfee,
                b.Finalprice,
                b.Createdat))
            .ToListAsync(ct);

        var classSessionsRawTask = context.ClassSessions
            .AsNoTracking()
            .Select(l => new ClassSessionRaw(
                l.Status,
                l.Lessonprice,
                l.Issettled,
                l.Scheduledstart))
            .ToListAsync(ct);

        var usersRawTask = context.Users
            .AsNoTracking()
            .Select(u => new UserRaw(u.Primaryrole, u.Createdat))
            .ToListAsync(ct);

        var tutorProfilesRawTask = context.Tutorprofiles
            .AsNoTracking()
            .Select(t => new TutorProfileRaw(t.Profilestatus, t.Ispublic, t.Averagerating))
            .ToListAsync(ct);

        var withdrawalsRawTask = context.Withdrawalrequests
            .AsNoTracking()
            .Select(w => new WithdrawalRaw(w.Status, w.Amount, w.Requestedat, w.Processedat))
            .ToListAsync(ct);

        var walletFrozenTask = context.Wallets
            .AsNoTracking()
            .SumAsync(w => w.Frozenbalance ?? 0, ct);

        var walletTxRawTask = context.Wallettransactions
            .AsNoTracking()
            .Select(t => new WalletTxRaw(t.Transactiontype, t.Amount))
            .ToListAsync(ct);

        var subjectsTask = context.Subjects
            .AsNoTracking()
            .Select(s => new { s.Subjectid, s.Subjectname })
            .ToListAsync(ct);

        // Doanh thu bán gói AI credit: payment_transactions Succeeded, purpose=AiCreditPurchase.
        var aiCreditTxRawTask = context.PaymentTransactions
            .AsNoTracking()
            .Where(t => t.Purpose == PaymentTransactionPurpose.AiCreditPurchase
                        && t.Status == PaymentTransactionStatus.Succeeded)
            .Select(t => new { t.Amount, t.Paidat, t.Createdat })
            .ToListAsync(ct);

        await Task.WhenAll(
            bookingsRawTask, classSessionsRawTask, usersRawTask,
            tutorProfilesRawTask, withdrawalsRawTask, walletTxRawTask, subjectsTask, aiCreditTxRawTask);

        var allBookings = await bookingsRawTask;
        var allClassSessions = await classSessionsRawTask;
        var allUsers = await usersRawTask;
        var tutorProfiles = await tutorProfilesRawTask;
        var allWithdrawals = await withdrawalsRawTask;
        var totalFrozen = await walletFrozenTask;
        var walletTx = await walletTxRawTask;
        var subjects = await subjectsTask;
        // Dùng thời điểm thu tiền thật (Paidat) để quy về kỳ; fallback Createdat.
        var aiCreditTx = (await aiCreditTxRawTask)
            .Select(t => new { t.Amount, When = t.Paidat ?? t.Createdat })
            .ToList();

        // ─── Revenue Overview ────────────────────────────────────────────────
        var revenueBookings = allBookings
            .Where(b => RevenueBookingStatuses.Contains(b.Status ?? ""))
            .ToList();

        var bookingPlatformRevenue = revenueBookings.Sum(b => b.Platformfee ?? 0);
        var totalGrossVolume = revenueBookings.Sum(b => b.Finalprice ?? 0);

        // AI credit revenue (bán gói Homework Helper)
        var aiCreditRevenue = aiCreditTx.Sum(t => t.Amount);
        var aiCreditCurrentMonth = aiCreditTx
            .Where(t => t.When >= currentMonthStartUtc && t.When < currentMonthStartUtc.AddMonths(1))
            .Sum(t => t.Amount);
        var aiCreditPreviousMonth = aiCreditTx
            .Where(t => t.When >= previousMonthStartUtc && t.When < currentMonthStartUtc)
            .Sum(t => t.Amount);
        var aiCreditCurrentYear = aiCreditTx
            .Where(t => t.When >= currentYearStartUtc)
            .Sum(t => t.Amount);

        // TỔNG doanh thu = booking platform fee + AI credit (cộng vào tổng, theo yêu cầu).
        var totalPlatformRevenue = bookingPlatformRevenue + aiCreditRevenue;

        var currentMonthRevenue = revenueBookings
            .Where(b => b.Createdat >= currentMonthStartUtc &&
                        b.Createdat < currentMonthStartUtc.AddMonths(1))
            .Sum(b => b.Platformfee ?? 0) + aiCreditCurrentMonth;

        var previousMonthRevenue = revenueBookings
            .Where(b => b.Createdat >= previousMonthStartUtc &&
                        b.Createdat < currentMonthStartUtc)
            .Sum(b => b.Platformfee ?? 0) + aiCreditPreviousMonth;

        decimal? momGrowth = previousMonthRevenue == 0
            ? null
            : Math.Round((currentMonthRevenue - previousMonthRevenue) / previousMonthRevenue * 100, 1);

        var currentYearRevenue = revenueBookings
            .Where(b => b.Createdat >= currentYearStartUtc)
            .Sum(b => b.Platformfee ?? 0) + aiCreditCurrentYear;

        // ─── Booking Metrics ─────────────────────────────────────────────────
        var byStatus = allBookings
            .GroupBy(b => b.Status ?? "unknown")
            .Select(g => new BookingStatusCount { Status = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToList();

        var byMode = allBookings
            .GroupBy(b => TeachingMode.Online)
            .Select(g => new BookingTeachingModeCount { Mode = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToList();

        var activeCount = allBookings.Count(b => ActiveBookingStatuses.Contains(b.Status ?? ""));
        var completedCount = allBookings.Count(b => b.Status == BookingStatus.Completed);
        var cancelledCount = allBookings.Count(b =>
            b.Status is BookingStatus.Cancelled or BookingStatus.CancelledNoshow or BookingStatus.PaymentTimeout);
        var pendingTutorCount = allBookings.Count(b => b.Status == BookingStatus.PendingTutor);
        var newThisPeriod = allBookings.Count(b =>
            b.Createdat.HasValue && b.Createdat >= fromUtc && b.Createdat <= toUtc);

        // ─── ClassSession Metrics ──────────────────────────────────────────────────
        var completedClassSessions = allClassSessions.Count(l => l.Status == ClassSessionStatus.Completed);
        var scheduledClassSessions = allClassSessions.Count(l =>
            l.Status is ClassSessionStatus.Scheduled or ClassSessionStatus.InProgress);
        var noShowClassSessions = allClassSessions.Count(l => l.Status == ClassSessionStatus.NoShow);
        var cancelledClassSessions = allClassSessions.Count(l =>
            l.Status is ClassSessionStatus.Cancelled or ClassSessionStatus.CancelledNoshow);
        var disputedClassSessions = allClassSessions.Count(l => l.Status == ClassSessionStatus.Disputed);

        var denom = completedClassSessions + cancelledClassSessions + noShowClassSessions;
        decimal? completionRate = denom == 0
            ? null
            : Math.Round((decimal)completedClassSessions / denom * 100, 1);
        decimal? noShowRate = denom == 0
            ? null
            : Math.Round((decimal)noShowClassSessions / denom * 100, 1);

        var totalClassSessionRevenue = allClassSessions
            .Where(l => l.Status == ClassSessionStatus.Completed && l.Issettled == true)
            .Sum(l => l.Lessonprice ?? 0);

        // ─── User Growth ─────────────────────────────────────────────────────
        var tutors = allUsers.Where(u => u.Role == UserRole.Tutor).ToList();
        var parents = allUsers.Where(u => u.Role == UserRole.Parent).ToList();
        var students = allUsers.Where(u => u.Role == UserRole.Student).ToList();
        var activeTutors = tutorProfiles.Count(t =>
            t.ProfileStatus == TutorProfileStatus.Active && t.IsPublic == true);
        var newTutorsThisMonth = tutors.Count(u =>
            u.CreatedAt.HasValue && u.CreatedAt >= currentMonthStartUtc);
        var newParentsThisMonth = parents.Count(u =>
            u.CreatedAt.HasValue && u.CreatedAt >= currentMonthStartUtc);

        var ratingsWithValue = tutorProfiles
            .Where(t => t.AverageRating is > 0)
            .Select(t => (double)t.AverageRating!.Value)
            .ToList();
        decimal? avgRating = ratingsWithValue.Count > 0
            ? Math.Round((decimal)ratingsWithValue.Average(), 2)
            : null;

        // ─── Withdrawal Metrics ──────────────────────────────────────────────
        var pendingW = allWithdrawals
            .Where(w => PendingWithdrawalStatuses.Contains(w.Status ?? "")).ToList();
        var completedW = allWithdrawals
            .Where(w => w.Status == WithdrawalStatus.Completed).ToList();
        var rejectedW = allWithdrawals
            .Where(w => w.Status == WithdrawalStatus.Rejected).ToList();
        var cancelledW = allWithdrawals
            .Where(w => w.Status == WithdrawalStatus.Cancelled).ToList();

        var processedThisMonth = allWithdrawals.Count(w =>
            w.ProcessedAt >= currentMonthStartUtc &&
            w.Status == WithdrawalStatus.Completed);
        var processedAmtThisMonth = allWithdrawals
            .Where(w => w.ProcessedAt >= currentMonthStartUtc &&
                        w.Status == WithdrawalStatus.Completed)
            .Sum(w => w.Amount ?? 0);

        // ─── Escrow (wallet transactions) ────────────────────────────────────
        var totalReleasedToTutors = walletTx
            .Where(t => t.Type == TransactionType.EscrowRelease)
            .Sum(t => t.Amount ?? 0);

        // Refund amounts are stored as positive credits back to parent wallet
        var totalRefundedToParents = walletTx
            .Where(t => t.Type == TransactionType.Refund && (t.Amount ?? 0) > 0)
            .Sum(t => t.Amount ?? 0);

        // ─── Revenue Trend ───────────────────────────────────────────────────
        var trendBookings = allBookings
            .Where(b => b.Createdat.HasValue &&
                        b.Createdat >= fromUtc && b.Createdat <= toUtc)
            .ToList();

        var trendClassSessions = allClassSessions
            .Where(l => l.Status == ClassSessionStatus.Completed &&
                        l.ScheduledStart >= fromUtc && l.ScheduledStart <= toUtc)
            .ToList();

        var revenueTrend = BuildTrend(trendBookings, trendClassSessions, period, fromUtc, toUtc);

        // ─── Top Subjects ────────────────────────────────────────────────────
        var subjectDict = subjects.ToDictionary(s => s.Subjectid, s => s.Subjectname ?? "Unknown");
        var topSubjects = allBookings
            .Where(b => b.SubjectId.HasValue)
            .GroupBy(b => b.SubjectId!.Value)
            .Select(g => new TopSubjectItem
            {
                SubjectId = g.Key,
                SubjectName = subjectDict.GetValueOrDefault(g.Key, "Unknown"),
                BookingCount = g.Count(),
                TotalRevenue = g.Sum(b => b.Platformfee ?? 0)
            })
            .OrderByDescending(x => x.BookingCount)
            .Take(10)
            .ToList();

        // ─── Assemble ────────────────────────────────────────────────────────
        return new AdminFinancialMetricsResponse
        {
            FilterFrom = from,
            FilterTo = to,
            Period = period,

            Revenue = new RevenueOverviewMetrics
            {
                TotalPlatformRevenue = totalPlatformRevenue,
                TotalGrossVolume = totalGrossVolume,
                CurrentMonthRevenue = currentMonthRevenue,
                PreviousMonthRevenue = previousMonthRevenue,
                MonthOverMonthGrowthPercent = momGrowth,
                CurrentYearRevenue = currentYearRevenue,
                TotalEscrowed = totalFrozen,
                BySource = new RevenueBySource
                {
                    Booking = bookingPlatformRevenue,
                    AiCredit = aiCreditRevenue
                }
            },

            Bookings = new BookingMetrics
            {
                Total = allBookings.Count,
                Active = activeCount,
                Completed = completedCount,
                Cancelled = cancelledCount,
                PendingTutor = pendingTutorCount,
                NewThisPeriod = newThisPeriod,
                ByStatus = byStatus,
                ByTeachingMode = byMode
            },

            ClassSessions = new ClassSessionMetrics
            {
                TotalCompleted = completedClassSessions,
                TotalScheduled = scheduledClassSessions,
                TotalNoShow = noShowClassSessions,
                TotalCancelled = cancelledClassSessions,
                TotalDisputed = disputedClassSessions,
                CompletionRatePercent = completionRate,
                NoShowRatePercent = noShowRate,
                TotalClassSessionRevenue = totalClassSessionRevenue
            },

            Users = new UserGrowthMetrics
            {
                TotalTutors = tutors.Count,
                TotalParents = parents.Count,
                TotalStudents = students.Count,
                ActiveTutors = activeTutors,
                NewTutorsThisMonth = newTutorsThisMonth,
                NewParentsThisMonth = newParentsThisMonth,
                AverageTutorRating = avgRating
            },

            Withdrawals = new WithdrawalMetrics
            {
                TotalPending = pendingW.Count,
                TotalPendingAmount = pendingW.Sum(w => w.Amount ?? 0),
                TotalApproved = completedW.Count,
                TotalApprovedAmount = completedW.Sum(w => w.Amount ?? 0),
                TotalRejected = rejectedW.Count,
                TotalRejectedAmount = rejectedW.Sum(w => w.Amount ?? 0),
                TotalCancelled = cancelledW.Count,
                TotalCancelledAmount = cancelledW.Sum(w => w.Amount ?? 0),
                ProcessedThisMonth = processedThisMonth,
                ProcessedAmountThisMonth = processedAmtThisMonth
            },

            Escrow = new EscrowMetrics
            {
                TotalFrozenBalance = totalFrozen,
                TotalReleasedToTutors = totalReleasedToTutors,
                TotalRefundedToParents = totalRefundedToParents
            },

            RevenueTrend = revenueTrend,
            TopSubjects = topSubjects
        };
    }

    // ─── Transactions ─────────────────────────────────────────────────────────

    public async Task<AdminTransactionListResponse> GetTransactionsAsync(
        int page,
        int pageSize,
        string? type,
        string? userId,
        DateTime? from,
        DateTime? to,
        string? search,
        CancellationToken ct = default)
    {
        // Xây query từ Wallettransactions JOIN Wallets JOIN Users
        var query = context.Wallettransactions
            .AsNoTracking()
            .Join(context.Wallets.AsNoTracking(),
                tx => tx.Walletid,
                w  => w.Walletid,
                (tx, w) => new { tx, w })
            .Join(context.Users.AsNoTracking(),
                joined => joined.w.Userid,
                u      => u.Userid,
                (joined, u) => new
                {
                    joined.tx.Transactionid,
                    joined.tx.Walletid,
                    UserId       = u.Userid,
                    UserFullName = u.Fullname,
                    UserEmail    = u.Email,
                    UserRole     = u.Primaryrole,
                    joined.tx.Amount,
                    Type         = joined.tx.Transactiontype,
                    joined.tx.Description,
                    joined.tx.Referenceid,
                    joined.tx.Referencetable,
                    joined.tx.Ordercode,
                    joined.tx.Createdat
                });

        // Áp filter
        if (!string.IsNullOrEmpty(type))
            query = query.Where(x => x.Type == type);

        if (!string.IsNullOrEmpty(userId))
            query = query.Where(x => x.UserId == userId);

        if (from.HasValue)
        {
            var fromUtc = from.Value.Kind == DateTimeKind.Utc
                ? from.Value
                : DateTime.SpecifyKind(from.Value, DateTimeKind.Utc);
            query = query.Where(x => x.Createdat >= fromUtc);
        }
        if (to.HasValue)
        {
            var toUtc = to.Value.Kind == DateTimeKind.Utc
                ? to.Value
                : DateTime.SpecifyKind(to.Value, DateTimeKind.Utc);
            query = query.Where(x => x.Createdat <= toUtc);
        }

        if (!string.IsNullOrEmpty(search))
        {
            var kw = search.ToLower();
            query = query.Where(x =>
                (x.Description != null && x.Description.ToLower().Contains(kw)) ||
                (x.UserFullName != null && x.UserFullName.ToLower().Contains(kw)) ||
                (x.UserEmail    != null && x.UserEmail.ToLower().Contains(kw)));
        }

        // Total count + sum trước khi phân trang
        var totalCount  = await query.CountAsync(ct);
        var totalAmount = totalCount > 0 ? await query.SumAsync(x => x.Amount ?? 0, ct) : 0;

        // Fetch trang (anonymous type → in-memory map với VietnamTimeHelper)
        var raw = await query
            .OrderByDescending(x => x.Createdat)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = raw.Select(x => new AdminTransactionItem
        {
            TransactionId = x.Transactionid,
            WalletId      = x.Walletid,
            UserId        = x.UserId,
            UserFullName  = x.UserFullName,
            UserEmail     = x.UserEmail,
            UserRole      = x.UserRole,
            Amount        = x.Amount,
            TransactionType = x.Type,
            Description   = x.Description,
            ReferenceId   = x.Referenceid,
            ReferenceTable = x.Referencetable,
            OrderCode     = x.Ordercode,
            CreatedAt     = x.Createdat
        }).ToList();

        return new AdminTransactionListResponse
        {
            Items       = items,
            TotalCount  = totalCount,
            TotalAmount = totalAmount,
            Page        = page,
            PageSize    = pageSize
        };
    }

    // ─── Real-money transaction audit ───────────────────────────────────────

    public async Task<AdminPaymentTransactionListResponse> GetPaymentTransactionsAsync(
        int page,
        int pageSize,
        string? paymentMethod,
        string? direction,
        string? purpose,
        string? status,
        string? reconciliationStatus,
        string? userId,
        int? bookingId,
        int? withdrawalId,
        int? paymentRequestId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? search,
        CancellationToken ct = default)
    {
        var query = context.PaymentTransactions.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(paymentMethod))
            query = query.Where(t => t.Paymentmethod == paymentMethod.Trim());
        if (!string.IsNullOrWhiteSpace(direction))
            query = query.Where(t => t.Direction == direction.Trim());
        if (!string.IsNullOrWhiteSpace(purpose))
            query = query.Where(t => t.Purpose == purpose.Trim());
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(t => t.Status == status.Trim());
        if (!string.IsNullOrWhiteSpace(reconciliationStatus))
            query = query.Where(t => t.Reconciliationstatus == reconciliationStatus.Trim());
        if (!string.IsNullOrWhiteSpace(userId))
            query = query.Where(t => t.Userid == userId.Trim());
        if (bookingId.HasValue)
            query = query.Where(t => t.Bookingid == bookingId.Value);
        if (withdrawalId.HasValue)
            query = query.Where(t => t.Withdrawalid == withdrawalId.Value);
        if (paymentRequestId.HasValue)
            query = query.Where(t => t.Paymentrequestid == paymentRequestId.Value);

        if (from.HasValue)
        {
            var fromUtc = from.Value.UtcDateTime;
            query = query.Where(t => (t.Paidat ?? t.Createdat) >= fromUtc);
        }

        if (to.HasValue)
        {
            var toUtc = to.Value.UtcDateTime;
            query = query.Where(t => (t.Paidat ?? t.Createdat) <= toUtc);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var keyword = search.Trim().ToLower();
            query = query.Where(t =>
                (t.Providertransactionid != null && t.Providertransactionid.ToLower().Contains(keyword))
                || (t.Paymentlinkid != null && t.Paymentlinkid.ToLower().Contains(keyword))
                || (t.Description != null && t.Description.ToLower().Contains(keyword))
                || (t.Note != null && t.Note.ToLower().Contains(keyword))
                || (t.Sourceaccountnumber != null && t.Sourceaccountnumber.ToLower().Contains(keyword))
                || (t.Destinationaccountnumber != null && t.Destinationaccountnumber.ToLower().Contains(keyword))
                || (t.Destinationvirtualaccountnumber != null && t.Destinationvirtualaccountnumber.ToLower().Contains(keyword))
                || (t.User != null && t.User.Fullname != null && t.User.Fullname.ToLower().Contains(keyword))
                || (t.User != null && t.User.Email.ToLower().Contains(keyword)));
        }

        var totalCount = await query.CountAsync(ct);
        var totalInboundAmount = await query
            .Where(t => t.Direction == PaymentTransactionDirection.Inbound)
            .SumAsync(t => (decimal?)t.Amount, ct) ?? 0;
        var totalOutboundAmount = await query
            .Where(t => t.Direction == PaymentTransactionDirection.Outbound)
            .SumAsync(t => (decimal?)t.Amount, ct) ?? 0;

        var transactions = await query
            .Include(t => t.User)
            .Include(t => t.ProcessedbyNavigation)
            .OrderByDescending(t => t.Paidat ?? t.Createdat)
            .ThenByDescending(t => t.Paymenttransactionid)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new AdminPaymentTransactionListResponse
        {
            Items = transactions.Select(MapPaymentTransaction<AdminPaymentTransactionItem>).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalInboundAmount = totalInboundAmount,
            TotalOutboundAmount = totalOutboundAmount
        };
    }

    public async Task<AdminPaymentTransactionDetailResponse> GetPaymentTransactionDetailAsync(
        int paymentTransactionId,
        CancellationToken ct = default)
    {
        var transaction = await context.PaymentTransactions
            .AsNoTracking()
            .Include(t => t.User)
            .Include(t => t.ProcessedbyNavigation)
            .FirstOrDefaultAsync(t => t.Paymenttransactionid == paymentTransactionId, ct)
            ?? throw new TransactionNotFoundException();

        var response = MapPaymentTransaction<AdminPaymentTransactionDetailResponse>(transaction);
        response.SourceAccountBankId = transaction.Sourceaccountbankid;
        response.SourceAccountBankName = transaction.Sourceaccountbankname;
        response.SourceAccountNumber = transaction.Sourceaccountnumber;
        response.SourceAccountName = transaction.Sourceaccountname;
        response.DestinationAccountBankBin = transaction.Destinationaccountbankbin;
        response.DestinationAccountBankName = transaction.Destinationaccountbankname;
        response.DestinationAccountNumber = transaction.Destinationaccountnumber;
        response.DestinationAccountName = transaction.Destinationaccountname;
        response.DestinationVirtualAccountNumber = transaction.Destinationvirtualaccountnumber;
        response.DestinationVirtualAccountName = transaction.Destinationvirtualaccountname;
        response.WebhookCode = transaction.Webhookcode;
        response.WebhookDescription = transaction.Webhookdesc;
        response.WebhookSuccess = transaction.Webhooksuccess;
        response.ProviderCode = transaction.Providercode;
        response.ProviderDescription = transaction.Providerdesc;
        response.ProviderPayload = ParseJsonPayload(transaction.Providerpayload);
        response.WebhookPayload = ParseJsonPayload(transaction.Webhookpayload);
        return response;
    }

    private static T MapPaymentTransaction<T>(PaymentTransaction transaction)
        where T : AdminPaymentTransactionItem, new()
    {
        return new T
        {
            PaymentTransactionId = transaction.Paymenttransactionid,
            UserId = transaction.Userid,
            UserFullName = transaction.User?.Fullname,
            UserEmail = transaction.User?.Email,
            UserRole = transaction.User?.Primaryrole,
            PaymentMethod = transaction.Paymentmethod,
            Direction = transaction.Direction,
            Purpose = transaction.Purpose,
            Status = transaction.Status,
            CaptureSource = transaction.Capturesource,
            ReconciliationStatus = transaction.Reconciliationstatus,
            CaptureFingerprint = transaction.Capturefingerprint,
            Amount = transaction.Amount,
            Currency = transaction.Currency,
            OrderCode = transaction.Ordercode,
            ProviderTransactionId = transaction.Providertransactionid,
            PaymentLinkId = transaction.Paymentlinkid,
            PaymentRequestId = transaction.Paymentrequestid,
            BookingId = transaction.Bookingid,
            WithdrawalId = transaction.Withdrawalid,
            Description = transaction.Description,
            PaidAt = AsUtc(transaction.Paidat),
            CreatedAt = AsUtc(transaction.Createdat),
            ProcessedBy = transaction.Processedby,
            ProcessedByName = transaction.ProcessedbyNavigation?.Fullname,
            Note = transaction.Note
        };
    }

    private static DateTime? AsUtc(DateTime? value)
    {
        if (!value.HasValue)
            return null;

        return value.Value.Kind == DateTimeKind.Utc
            ? value.Value
            : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);
    }

    private static JsonElement? ParseJsonPayload(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return null;

        try
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ─── Trend builder ────────────────────────────────────────────────────────

    private static List<RevenueTrendItem> BuildTrend(
        List<BookingRaw> bookings,
        List<ClassSessionRaw> classSessions,
        string period,
        DateTime fromUtc,
        DateTime toUtc)
    {
        var bookingGroups = bookings
            .GroupBy(b => GetBucket(b.Createdat ?? DateTime.MinValue, period))
            .ToDictionary(g => g.Key, g => g.ToList());

        var classSessionGroups = classSessions
            .GroupBy(l => GetBucket(l.ScheduledStart, period))
            .ToDictionary(g => g.Key, g => g.ToList());

        return GenerateBuckets(fromUtc, toUtc, period).Select(bucket =>
        {
            var bList = bookingGroups.GetValueOrDefault(bucket) ?? [];
            var lList = classSessionGroups.GetValueOrDefault(bucket) ?? [];
            return new RevenueTrendItem
            {
                Label = bucket,
                PlatformRevenue = bList.Sum(b => b.Platformfee ?? 0),
                GrossVolume = bList.Sum(b => b.Finalprice ?? 0),
                BookingCount = bList.Count,
                ClassSessionsCompleted = lList.Count
            };
        }).ToList();
    }

    private static string GetBucket(DateTime utcDate, string period)
    {
        if (utcDate == DateTime.MinValue) return "unknown";
        var vn = utcDate;
        return period switch
        {
            "week" => $"{ISOWeek.GetYear(vn)}-W{ISOWeek.GetWeekOfYear(vn):D2}",
            "year" => vn.Year.ToString(),
            _ => $"{vn.Year}-{vn.Month:D2}"
        };
    }

    private static List<string> GenerateBuckets(DateTime fromUtc, DateTime toUtc, string period)
    {
        var buckets = new List<string>();
        var vnFrom = fromUtc;
        var vnTo = toUtc;

        if (period == "year")
        {
            for (var y = vnFrom.Year; y <= vnTo.Year; y++)
                buckets.Add(y.ToString());
        }
        else if (period == "week")
        {
            // Start from the Monday of the week containing fromUtc
            var cursor = vnFrom.Date;
            var dow = (int)cursor.DayOfWeek;
            cursor = cursor.AddDays(dow == 0 ? -6 : -(dow - 1));
            while (cursor <= vnTo.Date)
            {
                buckets.Add($"{ISOWeek.GetYear(cursor)}-W{ISOWeek.GetWeekOfYear(cursor):D2}");
                cursor = cursor.AddDays(7);
            }
        }
        else // month
        {
            var cursor = new DateTime(vnFrom.Year, vnFrom.Month, 1);
            var end = new DateTime(vnTo.Year, vnTo.Month, 1);
            while (cursor <= end)
            {
                buckets.Add($"{cursor.Year}-{cursor.Month:D2}");
                cursor = cursor.AddMonths(1);
            }
        }

        return buckets;
    }

    // ─── Private projection records (avoid anonymous-type issues in LINQ) ────

    private sealed record BookingRaw(
        string? Status,
        string? TeachingMode,
        int? SubjectId,
        decimal? Platformfee,
        decimal? Finalprice,
        DateTime? Createdat);

    private sealed record ClassSessionRaw(
        string? Status,
        decimal? Lessonprice,
        bool? Issettled,
        DateTime ScheduledStart);

    private sealed record UserRaw(string? Role, DateTime? CreatedAt);

    private sealed record TutorProfileRaw(string? ProfileStatus, bool? IsPublic, double? AverageRating);

    private sealed record WithdrawalRaw(
        string? Status,
        decimal? Amount,
        DateTime? RequestedAt,
        DateTime? ProcessedAt);

    private sealed record WalletTxRaw(string? Type, decimal? Amount);
}
