using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.ResponseModel.Admin;
using MV.DomainLayer.Helpers;

namespace MV.ApplicationLayer.Services;

public class AdminDashboardService(
    IAppDbContext context,
    ILogger<AdminDashboardService> logger) : IAdminDashboardService
{
    private static readonly string[] ActiveBookingStatuses =
    [
        BookingStatus.Paid,
        BookingStatus.DepositPaid,
        BookingStatus.PendingRemainingPayment,
        BookingStatus.Ongoing
    ];

    private static readonly string[] CancelledBookingStatuses =
    [
        BookingStatus.Cancelled,
        BookingStatus.CancelledNoshow,
        BookingStatus.PaymentTimeout
    ];

    private static readonly string[] PendingWithdrawalStatuses =
    [
        WithdrawalStatus.Pending,
        WithdrawalStatus.PendingReview,
        WithdrawalStatus.Delayed,
        WithdrawalStatus.Approved
    ];

    // ─── API 1: GET /api/admin/dashboard/stats ────────────────────────────────

    public async Task<AdminDashboardStatsResponse> GetStatsAsync(CancellationToken ct = default)
    {
        logger.LogInformation("AdminDashboardService.GetStatsAsync");

        var vnNow = VietnamTimeHelper.Now;
        var monthStartUtc = new DateTime(vnNow.Year, vnNow.Month, 1, 0, 0, 0, DateTimeKind.Unspecified).AddHours(-7);
        var todayStartUtc = vnNow.Date.AddHours(-7);
        var todayEndUtc = todayStartUtc.AddDays(1);
        var nowUtc = MV.DomainLayer.Helpers.VietnamTimeHelper.Now;

        // Sequential fetch — EF Core DbContext is not thread-safe, cannot run concurrent queries
        var users = await context.Users
            .AsNoTracking()
            .Select(u => new { u.Primaryrole })
            .ToListAsync(ct);

        var tutorProfiles = await context.Tutorprofiles
            .AsNoTracking()
            .Select(t => new { t.Profilestatus, t.Ispublic })
            .ToListAsync(ct);

        var bookings = await context.Bookings
            .AsNoTracking()
            .Select(b => new { b.Status, b.Finalprice, b.Platformfee, b.Createdat })
            .ToListAsync(ct);

        var lessons = await context.Lessons
            .AsNoTracking()
            .Select(l => new { l.Status, l.Scheduledstart })
            .ToListAsync(ct);

        var withdrawals = await context.Withdrawalrequests
            .AsNoTracking()
            .Select(w => new { w.Status, w.Amount })
            .ToListAsync(ct);

        var disputes = await context.Disputes
            .AsNoTracking()
            .Select(d => new { d.Status })
            .ToListAsync(ct);

        var warningsCount = await context.Userwarnings
            .AsNoTracking()
            .CountAsync(ct);

        var activeSuspensions = await context.Profilesuspensions
            .AsNoTracking()
            .CountAsync(s => s.Isactive == true && (s.Enddate == null || s.Enddate > nowUtc), ct);

        // Platform overview
        var totalTutors = users.Count(u => u.Primaryrole == UserRole.Tutor);
        var totalParents = users.Count(u => u.Primaryrole == UserRole.Parent);
        var totalStudents = users.Count(u => u.Primaryrole == UserRole.Student);
        var pendingApprovals = tutorProfiles.Count(t => t.Profilestatus == TutorProfileStatus.PendingApproval);
        var activeTutors = tutorProfiles.Count(t => t.Profilestatus == TutorProfileStatus.Active && t.Ispublic == true);

        // Booking summary
        var activeBookings = bookings.Count(b => ActiveBookingStatuses.Contains(b.Status ?? ""));
        var completedBookings = bookings.Count(b => b.Status == BookingStatus.Completed);
        var cancelledBookings = bookings.Count(b => CancelledBookingStatuses.Contains(b.Status ?? ""));

        var gmvThisMonth = bookings
            .Where(b => b.Createdat >= monthStartUtc && b.Createdat < monthStartUtc.AddMonths(1))
            .Sum(b => b.Finalprice ?? 0);
        var revenueThisMonth = bookings
            .Where(b => b.Createdat >= monthStartUtc && b.Createdat < monthStartUtc.AddMonths(1))
            .Sum(b => b.Platformfee ?? 0);

        // Lesson summary
        var lessonsToday = lessons.Count(l =>
            l.Scheduledstart >= todayStartUtc && l.Scheduledstart < todayEndUtc);

        var completedCount = lessons.Count(l => l.Status == LessonStatus.Completed);
        var cancelledCount = lessons.Count(l => l.Status is LessonStatus.Cancelled or LessonStatus.CancelledNoshow);
        var noShowCount = lessons.Count(l => l.Status == LessonStatus.NoShow);
        var denom = completedCount + cancelledCount + noShowCount;
        decimal? completionRate = denom == 0 ? null : Math.Round((decimal)completedCount / denom * 100, 1);
        decimal? noShowRate = denom == 0 ? null : Math.Round((decimal)noShowCount / denom * 100, 1);

        // Pending actions
        var pendingWithdrawals = withdrawals.Where(w => PendingWithdrawalStatuses.Contains(w.Status ?? "")).ToList();
        var openDisputes = disputes.Count(d => d.Status is DisputeStatus.Pending or DisputeStatus.Investigating);

        return new AdminDashboardStatsResponse
        {
            PlatformOverview = new DashboardPlatformOverview
            {
                TotalUsers = users.Count,
                TotalActiveTutors = activeTutors,
                TotalParents = totalParents,
                TotalStudents = totalStudents,
                PendingTutorApprovals = pendingApprovals,
                SuspendedUsers = activeSuspensions
            },
            BookingSummary = new DashboardBookingSummary
            {
                ActiveBookings = activeBookings,
                CompletedBookings = completedBookings,
                CancelledBookings = cancelledBookings,
                GmvThisMonth = gmvThisMonth,
                PlatformRevenueThisMonth = revenueThisMonth
            },
            LessonSummary = new DashboardLessonSummary
            {
                LessonsToday = lessonsToday,
                CompletionRatePercent = completionRate,
                NoShowRatePercent = noShowRate
            },
            PendingActions = new DashboardPendingActions
            {
                PendingWithdrawals = pendingWithdrawals.Count,
                PendingWithdrawalAmount = pendingWithdrawals.Sum(w => w.Amount ?? 0),
                OpenDisputes = openDisputes,
                PendingWarnings = warningsCount
            }
        };
    }

    // ─── API 2: GET /api/admin/dashboard/users ────────────────────────────────

    public async Task<AdminUserStatsResponse> GetUserStatsAsync(
        DateTime? from,
        DateTime? to,
        CancellationToken ct = default)
    {
        var nowUtc = MV.DomainLayer.Helpers.VietnamTimeHelper.Now;
        var toUtc = to ?? nowUtc;
        var fromUtc = from ?? toUtc.AddDays(-30);

        var vnNow = VietnamTimeHelper.Now;
        var weekStartUtc = nowUtc.AddDays(-7);
        var monthStartUtc = new DateTime(vnNow.Year, vnNow.Month, 1, 0, 0, 0, DateTimeKind.Unspecified).AddHours(-7);

        logger.LogInformation(
            "AdminDashboardService.GetUserStatsAsync from={From} to={To}", fromUtc, toUtc);

        // Sequential fetch — EF Core DbContext is not thread-safe, cannot run concurrent queries
        var users = await context.Users
            .AsNoTracking()
            .Select(u => new { u.Primaryrole, u.Createdat })
            .ToListAsync(ct);

        var tutorProfiles = await context.Tutorprofiles
            .AsNoTracking()
            .Select(t => new { t.Profilestatus, t.Ispublic })
            .ToListAsync(ct);

        var activeSuspensions = await context.Profilesuspensions
            .AsNoTracking()
            .CountAsync(s => s.Isactive == true && (s.Enddate == null || s.Enddate > nowUtc), ct);

        var warnings = await context.Userwarnings
            .AsNoTracking()
            .Select(w => new { w.Userid })
            .ToListAsync(ct);

        var tutors = users.Where(u => u.Primaryrole == UserRole.Tutor).ToList();
        var parents = users.Where(u => u.Primaryrole == UserRole.Parent).ToList();
        var students = users.Where(u => u.Primaryrole == UserRole.Student).ToList();
        var staff = users.Where(u => u.Primaryrole == UserRole.Staff).ToList();

        // Growth trong khoảng from–to
        var newTutors = tutors.Count(u => u.Createdat >= fromUtc && u.Createdat <= toUtc);
        var newParents = parents.Count(u => u.Createdat >= fromUtc && u.Createdat <= toUtc);
        var newStudents = students.Count(u => u.Createdat >= fromUtc && u.Createdat <= toUtc);
        var newThisWeek = users.Count(u => u.Createdat >= weekStartUtc);
        var newThisMonth = users.Count(u => u.Createdat >= monthStartUtc);

        return new AdminUserStatsResponse
        {
            FilterFrom = VietnamTimeHelper.ToVietnamTime(fromUtc),
            FilterTo = VietnamTimeHelper.ToVietnamTime(toUtc),
            ByRole = new UserStatsByRole
            {
                TotalTutors = tutors.Count,
                TotalParents = parents.Count,
                TotalStudents = students.Count,
                TotalStaff = staff.Count
            },
            TutorFunnel = new UserStatsTutorFunnel
            {
                Draft = tutorProfiles.Count(t => t.Profilestatus == TutorProfileStatus.Draft),
                PendingApproval = tutorProfiles.Count(t => t.Profilestatus == TutorProfileStatus.PendingApproval),
                Active = tutorProfiles.Count(t => t.Profilestatus == TutorProfileStatus.Active),
                Rejected = tutorProfiles.Count(t => t.Profilestatus == TutorProfileStatus.Rejected),
                PublicTutors = tutorProfiles.Count(t => t.Profilestatus == TutorProfileStatus.Active && t.Ispublic == true)
            },
            Growth = new UserStatsGrowth
            {
                NewTutors = newTutors,
                NewParents = newParents,
                NewStudents = newStudents,
                NewUsersThisWeek = newThisWeek,
                NewUsersThisMonth = newThisMonth
            },
            Moderation = new UserStatsModeration
            {
                ActiveSuspensions = activeSuspensions,
                TotalWarnings = warnings.Count,
                UsersWithWarnings = warnings.Select(w => w.Userid).Distinct().Count()
            }
        };
    }

    // ─── API 3: GET /api/admin/dashboard/tutor-performance ───────────────────

    public async Task<AdminTutorPerformanceResponse> GetTutorPerformanceAsync(
        int top,
        DateTime? from,
        DateTime? to,
        CancellationToken ct = default)
    {
        var nowUtc = MV.DomainLayer.Helpers.VietnamTimeHelper.Now;
        var toUtc = to ?? nowUtc;
        var fromUtc = from ?? toUtc.AddDays(-30);

        logger.LogInformation(
            "AdminDashboardService.GetTutorPerformanceAsync top={Top} from={From} to={To}",
            top, fromUtc, toUtc);

        // Sequential fetch — EF Core DbContext is not thread-safe, cannot run concurrent queries
        var tutorProfiles = await context.Tutorprofiles
            .AsNoTracking()
            .Where(t => t.Profilestatus == TutorProfileStatus.Active)
            .Select(t => new
            {
                t.Tutorid,
                t.Averagerating,
                t.Subscriptiontype,
                UserFullName = t.Tutor != null ? t.Tutor.Fullname : null,
                UserAvatar = t.Tutor != null ? t.Tutor.Avatarurl : null
            })
            .ToListAsync(ct);

        var lessons = await context.Lessons
            .AsNoTracking()
            .Where(l => l.Scheduledstart >= fromUtc && l.Scheduledstart <= toUtc)
            .Select(l => new
            {
                l.Tutorid,
                l.Status,
                l.Lessonprice,
                l.Issettled
            })
            .ToListAsync(ct);

        var feedbacks = await context.Feedbacks
            .AsNoTracking()
            .Where(f => f.Rating != null)
            .Select(f => new { f.Touserid, f.Rating, f.Feedbacktype, f.Createdat })
            .ToListAsync(ct);

        // Group lessons by tutor
        var lessonsByTutor = lessons.GroupBy(l => l.Tutorid ?? "").ToDictionary(g => g.Key, g => g.ToList());

        // Build per-tutor stats
        var tutorItems = tutorProfiles.Select(t =>
        {
            var tLessons = lessonsByTutor.GetValueOrDefault(t.Tutorid ?? "", []);
            var completed = tLessons.Count(l => l.Status == LessonStatus.Completed);
            var cancelled = tLessons.Count(l => l.Status is LessonStatus.Cancelled or LessonStatus.CancelledNoshow);
            var noShows = tLessons.Count(l => l.Status == LessonStatus.NoShow);
            var denom = completed + cancelled + noShows;
            decimal? rate = denom == 0 ? null : Math.Round((decimal)completed / denom * 100, 1);
            var revenue = tLessons
                .Where(l => l.Status == LessonStatus.Completed && l.Issettled == true)
                .Sum(l => l.Lessonprice ?? 0);

            return new TutorPerformanceItem
            {
                TutorId = t.Tutorid ?? "",
                FullName = t.UserFullName ?? "",
                AvatarUrl = t.UserAvatar,
                AverageRating = t.Averagerating.HasValue ? Math.Round((decimal)t.Averagerating.Value, 2) : null,
                TotalFeedbacks = feedbacks.Count(f => f.Touserid == t.Tutorid),
                LessonsCompleted = completed,
                LessonsCancelled = cancelled,
                NoShows = noShows,
                CompletionRatePercent = rate,
                TotalRevenue = revenue,
                SubscriptionType = t.Subscriptiontype
            };
        }).ToList();

        // Top lists
        var topN = Math.Max(1, Math.Min(top, 50));
        var topByRating = tutorItems
            .Where(t => t.AverageRating.HasValue)
            .OrderByDescending(t => t.AverageRating)
            .Take(topN).ToList();

        var topByLessons = tutorItems
            .OrderByDescending(t => t.LessonsCompleted)
            .Take(topN).ToList();

        var topByRevenue = tutorItems
            .OrderByDescending(t => t.TotalRevenue)
            .Take(topN).ToList();

        // Platform averages
        var ratingValues = tutorProfiles
            .Where(t => t.Averagerating is > 0 && t.Averagerating.HasValue)
            .Select(t => (decimal)t.Averagerating!.Value)
            .ToList();
        decimal? platformAvgRating = ratingValues.Count > 0 ? Math.Round(ratingValues.Average(), 2) : null;

        var completionRates = tutorItems
            .Where(t => t.CompletionRatePercent.HasValue)
            .Select(t => t.CompletionRatePercent!.Value)
            .ToList();
        decimal? platformAvgCompletion = completionRates.Count > 0 ? Math.Round(completionRates.Average(), 1) : null;

        // Feedback summary
        var allRatings = feedbacks.Where(f => f.Rating.HasValue).Select(f => f.Rating!.Value).ToList();
        decimal? avgRating = allRatings.Count > 0 ? Math.Round((decimal)allRatings.Average(), 2) : null;
        var satisfiedCount = allRatings.Count(r => r >= 4);
        decimal? satisfactionRate = allRatings.Count > 0
            ? Math.Round((decimal)satisfiedCount / allRatings.Count * 100, 1)
            : null;

        var ratingDist = Enumerable.Range(1, 5)
            .Select(r => new RatingDistributionItem { Rating = r, Count = allRatings.Count(x => x == r) })
            .ToList();

        return new AdminTutorPerformanceResponse
        {
            FilterFrom = VietnamTimeHelper.ToVietnamTime(fromUtc),
            FilterTo = VietnamTimeHelper.ToVietnamTime(toUtc),
            PlatformAverageRating = platformAvgRating,
            PlatformAvgCompletionRate = platformAvgCompletion,
            TopByRating = topByRating,
            TopByLessonsCompleted = topByLessons,
            TopByRevenue = topByRevenue,
            FeedbackSummary = new TutorFeedbackSummary
            {
                TotalFeedbacks = feedbacks.Count,
                AverageRating = avgRating,
                RatingDistribution = ratingDist,
                ParentSatisfactionRate = satisfactionRate
            }
        };
    }

    // ─── API 4: GET /api/admin/dashboard/disputes ────────────────────────────

    public async Task<AdminDisputeStatsResponse> GetDisputeStatsAsync(
        DateTime? from,
        DateTime? to,
        CancellationToken ct = default)
    {
        var nowUtc = MV.DomainLayer.Helpers.VietnamTimeHelper.Now;
        var toUtc = to ?? nowUtc;
        var fromUtc = from ?? toUtc.AddDays(-30);

        logger.LogInformation(
            "AdminDashboardService.GetDisputeStatsAsync from={From} to={To}", fromUtc, toUtc);

        var allDisputes = await context.Disputes
            .AsNoTracking()
            .Select(d => new
            {
                d.Status,
                d.Disputetype,
                d.Refundamount,
                d.Createdat,
                d.Resolvedat
            })
            .ToListAsync(ct);

        // Overview
        var total = allDisputes.Count;
        var pending = allDisputes.Count(d => d.Status == DisputeStatus.Pending);
        var investigating = allDisputes.Count(d => d.Status == DisputeStatus.Investigating);
        var resolved = allDisputes.Count(d => d.Status == DisputeStatus.Resolved);
        var closed = allDisputes.Count(d => d.Status == DisputeStatus.Closed);

        decimal? resolutionRate = total == 0
            ? null
            : Math.Round((decimal)(resolved + closed) / total * 100, 1);

        // Avg resolution days (chỉ tính dispute đã resolved/closed và có cả 2 timestamps)
        var resolutionTimes = allDisputes
            .Where(d => d.Status is DisputeStatus.Resolved or DisputeStatus.Closed
                        && d.Createdat.HasValue && d.Resolvedat.HasValue)
            .Select(d => (d.Resolvedat!.Value - d.Createdat!.Value).TotalDays)
            .ToList();
        decimal? avgResolutionDays = resolutionTimes.Count > 0
            ? Math.Round((decimal)resolutionTimes.Average(), 1)
            : null;

        // Financial — all-time refunds
        var totalRefund = allDisputes
            .Where(d => d.Status is DisputeStatus.Resolved or DisputeStatus.Closed)
            .Sum(d => d.Refundamount ?? 0);

        // Financial — in period
        var periodDisputes = allDisputes
            .Where(d => d.Resolvedat >= fromUtc && d.Resolvedat <= toUtc
                        && d.Status is DisputeStatus.Resolved or DisputeStatus.Closed)
            .ToList();
        var refundsThisPeriod = periodDisputes.Count;
        var refundAmtThisPeriod = periodDisputes.Sum(d => d.Refundamount ?? 0);

        // By type
        var byType = allDisputes
            .GroupBy(d => d.Disputetype ?? "other")
            .Select(g => new DisputeTypeCount { Type = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToList();

        // Monthly trend in range
        var trend = allDisputes
            .Where(d => d.Createdat >= fromUtc && d.Createdat <= toUtc)
            .GroupBy(d =>
            {
                var vn = VietnamTimeHelper.ToVietnamTime(d.Createdat!.Value);
                return $"{vn.Year}-{vn.Month:D2}";
            })
            .Select(g => new DisputeTrendItem
            {
                Month = g.Key,
                Count = g.Count(),
                RefundAmount = g.Sum(d => d.Refundamount ?? 0)
            })
            .OrderBy(x => x.Month)
            .ToList();

        return new AdminDisputeStatsResponse
        {
            FilterFrom = VietnamTimeHelper.ToVietnamTime(fromUtc),
            FilterTo = VietnamTimeHelper.ToVietnamTime(toUtc),
            Overview = new DisputeStatsOverview
            {
                TotalDisputes = total,
                Pending = pending,
                Investigating = investigating,
                Resolved = resolved,
                Closed = closed,
                ResolutionRatePercent = resolutionRate,
                AvgResolutionDays = avgResolutionDays
            },
            Financial = new DisputeStatsFinancial
            {
                TotalRefundAmount = totalRefund,
                RefundsThisPeriod = refundsThisPeriod,
                RefundAmountThisPeriod = refundAmtThisPeriod
            },
            ByType = byType,
            Trend = trend
        };
    }
}
