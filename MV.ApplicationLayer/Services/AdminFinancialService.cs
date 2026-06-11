using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.ResponseModel.Admin;
using MV.DomainLayer.Helpers;
using System.Globalization;

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

        var toUtc = to ?? MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
        var fromUtc = from ?? period switch
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
        var userNow = TimeZoneHelper.ToUserTime(nowUtc);
        var currentMonthStartUser = new DateTime(userNow.Year, userNow.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var currentMonthStartUtc = TimeZoneHelper.ToUtc(currentMonthStartUser);
        var previousMonthStartUtc = currentMonthStartUtc.AddMonths(-1);
        var currentYearStartUser = new DateTime(userNow.Year, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var currentYearStartUtc = TimeZoneHelper.ToUtc(currentYearStartUser);

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

        var lessonsRawTask = context.Lessons
            .AsNoTracking()
            .Select(l => new LessonRaw(
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

        await Task.WhenAll(
            bookingsRawTask, lessonsRawTask, usersRawTask,
            tutorProfilesRawTask, withdrawalsRawTask, walletTxRawTask, subjectsTask);

        var allBookings = await bookingsRawTask;
        var allLessons = await lessonsRawTask;
        var allUsers = await usersRawTask;
        var tutorProfiles = await tutorProfilesRawTask;
        var allWithdrawals = await withdrawalsRawTask;
        var totalFrozen = await walletFrozenTask;
        var walletTx = await walletTxRawTask;
        var subjects = await subjectsTask;

        // ─── Revenue Overview ────────────────────────────────────────────────
        var revenueBookings = allBookings
            .Where(b => RevenueBookingStatuses.Contains(b.Status ?? ""))
            .ToList();

        var totalPlatformRevenue = revenueBookings.Sum(b => b.Platformfee ?? 0);
        var totalGrossVolume = revenueBookings.Sum(b => b.Finalprice ?? 0);

        var currentMonthRevenue = revenueBookings
            .Where(b => b.Createdat >= currentMonthStartUtc &&
                        b.Createdat < currentMonthStartUtc.AddMonths(1))
            .Sum(b => b.Platformfee ?? 0);

        var previousMonthRevenue = revenueBookings
            .Where(b => b.Createdat >= previousMonthStartUtc &&
                        b.Createdat < currentMonthStartUtc)
            .Sum(b => b.Platformfee ?? 0);

        decimal? momGrowth = previousMonthRevenue == 0
            ? null
            : Math.Round((currentMonthRevenue - previousMonthRevenue) / previousMonthRevenue * 100, 1);

        var currentYearRevenue = revenueBookings
            .Where(b => b.Createdat >= currentYearStartUtc)
            .Sum(b => b.Platformfee ?? 0);

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

        // ─── Lesson Metrics ──────────────────────────────────────────────────
        var completedLessons = allLessons.Count(l => l.Status == LessonStatus.Completed);
        var scheduledLessons = allLessons.Count(l =>
            l.Status is LessonStatus.Scheduled or LessonStatus.InProgress);
        var noShowLessons = allLessons.Count(l => l.Status == LessonStatus.NoShow);
        var cancelledLessons = allLessons.Count(l =>
            l.Status is LessonStatus.Cancelled or LessonStatus.CancelledNoshow);
        var disputedLessons = allLessons.Count(l => l.Status == LessonStatus.Disputed);

        var denom = completedLessons + cancelledLessons + noShowLessons;
        decimal? completionRate = denom == 0
            ? null
            : Math.Round((decimal)completedLessons / denom * 100, 1);
        decimal? noShowRate = denom == 0
            ? null
            : Math.Round((decimal)noShowLessons / denom * 100, 1);

        var totalLessonRevenue = allLessons
            .Where(l => l.Status == LessonStatus.Completed && l.Issettled == true)
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

        var trendLessons = allLessons
            .Where(l => l.Status == LessonStatus.Completed &&
                        l.ScheduledStart >= fromUtc && l.ScheduledStart <= toUtc)
            .ToList();

        var revenueTrend = BuildTrend(trendBookings, trendLessons, period, fromUtc, toUtc);

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
            FilterFrom = from.HasValue ? TimeZoneHelper.ToUserTime(from.Value) : null,
            FilterTo = to.HasValue ? TimeZoneHelper.ToUserTime(to.Value) : null,
            Period = period,

            Revenue = new RevenueOverviewMetrics
            {
                TotalPlatformRevenue = totalPlatformRevenue,
                TotalGrossVolume = totalGrossVolume,
                CurrentMonthRevenue = currentMonthRevenue,
                PreviousMonthRevenue = previousMonthRevenue,
                MonthOverMonthGrowthPercent = momGrowth,
                CurrentYearRevenue = currentYearRevenue,
                TotalEscrowed = totalFrozen
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

            Lessons = new LessonMetrics
            {
                TotalCompleted = completedLessons,
                TotalScheduled = scheduledLessons,
                TotalNoShow = noShowLessons,
                TotalCancelled = cancelledLessons,
                TotalDisputed = disputedLessons,
                CompletionRatePercent = completionRate,
                NoShowRatePercent = noShowRate,
                TotalLessonRevenue = totalLessonRevenue
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
            query = query.Where(x => x.Createdat >= from.Value);

        if (to.HasValue)
            query = query.Where(x => x.Createdat <= to.Value);

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
            CreatedAt     = x.Createdat.HasValue
                ? TimeZoneHelper.ToUserTime(x.Createdat.Value)
                : (DateTime?)null
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

    // ─── Trend builder ────────────────────────────────────────────────────────

    private static List<RevenueTrendItem> BuildTrend(
        List<BookingRaw> bookings,
        List<LessonRaw> lessons,
        string period,
        DateTime fromUtc,
        DateTime toUtc)
    {
        var bookingGroups = bookings
            .GroupBy(b => GetBucket(b.Createdat ?? DateTime.MinValue, period))
            .ToDictionary(g => g.Key, g => g.ToList());

        var lessonGroups = lessons
            .GroupBy(l => GetBucket(l.ScheduledStart, period))
            .ToDictionary(g => g.Key, g => g.ToList());

        return GenerateBuckets(fromUtc, toUtc, period).Select(bucket =>
        {
            var bList = bookingGroups.GetValueOrDefault(bucket) ?? [];
            var lList = lessonGroups.GetValueOrDefault(bucket) ?? [];
            return new RevenueTrendItem
            {
                Label = bucket,
                PlatformRevenue = bList.Sum(b => b.Platformfee ?? 0),
                GrossVolume = bList.Sum(b => b.Finalprice ?? 0),
                BookingCount = bList.Count,
                LessonsCompleted = lList.Count
            };
        }).ToList();
    }

    private static string GetBucket(DateTime utcDate, string period)
    {
        if (utcDate == DateTime.MinValue) return "unknown";
        var vn = TimeZoneHelper.ToUserTime(utcDate);
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
        var vnFrom = TimeZoneHelper.ToUserTime(fromUtc);
        var vnTo = TimeZoneHelper.ToUserTime(toUtc);

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

    private sealed record LessonRaw(
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
