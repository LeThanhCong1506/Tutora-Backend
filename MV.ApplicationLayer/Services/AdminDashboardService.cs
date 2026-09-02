using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.DTO.ResponseModel.Admin;
using MV.DomainLayer.Helpers;

namespace MV.ApplicationLayer.Services;

public class AdminDashboardService(
    IAppDbContext context,
    IAdminRevenueAnalyticsService revenueAnalytics,
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

    /// <summary>
    /// Booking còn ĐANG chạy — phải khớp
    /// <c>AdminRevenueAnalyticsService.RevenueBookingStatuses</c>, gồm cả <c>pending_tutor</c>
    /// (vừa trả cọc, chờ gia sư nhận).
    ///
    /// Tự nó KHÔNG phải tập tính tiền: một booking bị huỷ giữa chừng đã rơi khỏi danh sách này
    /// nhưng phụ huynh vẫn mất tiền thật và Tutora vẫn giữ phí dịch vụ không hoàn của các buổi
    /// bị huỷ. Tập tính tiền là COHORT — xem <see cref="IsInRevenueCohort"/>.
    /// </summary>
    private static readonly string[] LiveBookingStatuses =
    [
        BookingStatus.PendingTutor,
        BookingStatus.Paid,
        BookingStatus.DepositPaid,
        BookingStatus.PendingRemainingPayment,
        BookingStatus.Ongoing,
        BookingStatus.Completed
    ];

    /// <summary>
    /// Booking được tính vào tiền: đang chạy, HOẶC đã đóng sổ mà phụ huynh thực sự đã trả tiền.
    ///
    /// Bản sao của <c>AdminRevenueAnalyticsService.CohortBookings</c>: đang chạy, HOẶC đã đóng
    /// sổ mà phụ huynh THỰC SỰ còn mất tiền (<c>CashIn − Refunded &gt; 0</c>). Trả rồi được hoàn
    /// 100% thì bị loại, giống hệt khoá chưa ai trả đồng nào — nếu không, một khoá đã đảo sạch
    /// vẫn thổi GMV lên theo giá hợp đồng.
    ///
    /// Chỉ dùng cho dữ liệu ĐÃ tải lên bộ nhớ (cần tra sổ ví, không dịch được xuống SQL). Truy
    /// vấn EF phải lọc bằng tập id dựng sẵn — xem <c>GetTrendsAsync</c>. Con số tổng ở
    /// <c>GetSummaryAsync</c> thì lấy thẳng từ <see cref="IAdminRevenueAnalyticsService"/> nên
    /// không phụ thuộc bản sao này.
    /// </summary>
    private static bool IsInRevenueCohort(
        string? status,
        DateTime? depositPaidAt,
        DateTime? remainingPaidAt,
        decimal finalPrice,
        decimal depositAmount,
        decimal refunded)
    {
        if (LiveBookingStatuses.Contains(status ?? "")) return true;

        // Khoá đã đóng sổ: chỉ tính khi phụ huynh THỰC SỰ còn mất tiền. Trả rồi được hoàn 100%
        // (huỷ trước buổi đầu, gia sư không phản hồi, no-show) thì về mặt kinh tế giống hệt
        // chưa từng trả — xem `CohortBookings` bên AdminRevenueAnalyticsService.
        var cashIn = remainingPaidAt != null ? finalPrice
            : depositPaidAt != null ? depositAmount
            : 0m;
        return cashIn - refunded > 0;
    }

    /// <summary>
    /// Tổng đã hoàn theo booking, lấy từ SỔ VÍ — <c>Booking.Refundamount</c> chỉ được ghi ở hai
    /// luồng huỷ nên không dùng được để đánh giá "đã hoàn hết chưa".
    /// </summary>
    private async Task<Dictionary<int, decimal>> LoadRefundsByBookingAsync(CancellationToken ct)
    {
        var rows = await context.Wallettransactions.AsNoTracking()
            .Where(t => t.Transactiontype == TransactionType.Refund
                        && t.Referencetable == ReferenceTable.Booking
                        && t.Referenceid != null)
            .Select(t => new { BookingId = t.Referenceid!.Value, Amount = t.Amount ?? 0 })
            .ToListAsync(ct);

        return rows.GroupBy(r => r.BookingId)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Amount));
    }

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

        var nowUtc = TimeZoneHelper.UtcNow;
        var userNow = nowUtc;
        var monthStartUser = new DateTime(userNow.Year, userNow.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var monthStartUtc = DateTime.SpecifyKind(monthStartUser, DateTimeKind.Utc);
        var todayStartUser = userNow.Date;
        var todayStartUtc = DateTime.SpecifyKind(todayStartUser, DateTimeKind.Utc);
        var todayEndUtc = todayStartUtc.AddDays(1);

        // Sequential fetch — EF Core DbContext is not thread-safe, cannot run concurrent queries
        var users = await context.Users
            .AsNoTracking()
            .Select(u => new { u.Primaryrole })
            .ToListAsync(ct);

        var tutorProfiles = await context.Tutorprofiles
            .AsNoTracking()
            .Select(t => new { t.Profilestatus, t.Ispublic })
            .ToListAsync(ct);

        var refundsByBooking = await LoadRefundsByBookingAsync(ct);

        var bookings = await context.Bookings
            .AsNoTracking()
            .Select(b => new
            {
                b.Bookingid, b.Status, b.Finalprice, b.Platformfee, b.Createdat,
                b.Depositpaidat, b.Remainingpaidat, b.Depositamount
            })
            .ToListAsync(ct);

        var classSessions = await context.ClassSessions
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

        var pendingCertificates = await context.Tutorcertificates
            .AsNoTracking()
            .CountAsync(c => c.Verificationstatus == CertificateStatus.PendingReview, ct);

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

        var revenueBookingsThisMonth = bookings
            .Where(b => b.Createdat >= monthStartUtc && b.Createdat < monthStartUtc.AddMonths(1)
                        && IsInRevenueCohort(
                            b.Status, b.Depositpaidat, b.Remainingpaidat,
                            b.Finalprice ?? 0, b.Depositamount ?? 0,
                            refundsByBooking.TryGetValue(b.Bookingid, out var rf) ? rf : 0));
        var gmvThisMonth = revenueBookingsThisMonth.Sum(b => b.Finalprice ?? 0);
        var revenueThisMonth = revenueBookingsThisMonth.Sum(b => b.Platformfee ?? 0);

        // ClassSession summary
        var classSessionsToday = classSessions.Count(l =>
            l.Scheduledstart >= todayStartUtc && l.Scheduledstart < todayEndUtc);

        var completedCount = classSessions.Count(l => l.Status == ClassSessionStatus.Completed);
        var cancelledCount = classSessions.Count(l => l.Status is ClassSessionStatus.Cancelled or ClassSessionStatus.CancelledNoshow);
        var noShowCount = classSessions.Count(l => l.Status == ClassSessionStatus.NoShow);
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
            ClassSessionSummary = new DashboardClassSessionSummary
            {
                ClassSessionsToday = classSessionsToday,
                CompletionRatePercent = completionRate,
                NoShowRatePercent = noShowRate
            },
            PendingActions = new DashboardPendingActions
            {
                PendingWithdrawals = pendingWithdrawals.Count,
                PendingWithdrawalAmount = pendingWithdrawals.Sum(w => w.Amount ?? 0),
                OpenDisputes = openDisputes,
                PendingWarnings = warningsCount,
                PendingCertificates = pendingCertificates
            }
        };
    }

    // ─── API 2: GET /api/admin/dashboard/users ────────────────────────────────

    public async Task<AdminUserStatsResponse> GetUserStatsAsync(
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken ct = default)
    {
        var nowUtc = TimeZoneHelper.UtcNow;
        var toUtc = to?.UtcDateTime ?? nowUtc;
        var fromUtc = from?.UtcDateTime ?? toUtc.AddDays(-30);

        var userNow = nowUtc;
        var weekStartUtc = nowUtc.AddDays(-7);
        var monthStartUser = new DateTime(userNow.Year, userNow.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var monthStartUtc = DateTime.SpecifyKind(monthStartUser, DateTimeKind.Utc);

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
            FilterFrom = fromUtc,
            FilterTo = toUtc,
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
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken ct = default)
    {
        var nowUtc = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
        var toUtc = to?.UtcDateTime ?? nowUtc;
        var fromUtc = from?.UtcDateTime ?? toUtc.AddDays(-30);

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

        var classSessions = await context.ClassSessions
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

        // Group classSessions by tutor
        var classSessionsByTutor = classSessions.GroupBy(l => l.Tutorid ?? "").ToDictionary(g => g.Key, g => g.ToList());

        // Build per-tutor stats
        var tutorItems = tutorProfiles.Select(t =>
        {
            var tClassSessions = classSessionsByTutor.GetValueOrDefault(t.Tutorid ?? "", []);
            var completed = tClassSessions.Count(l => l.Status == ClassSessionStatus.Completed);
            var cancelled = tClassSessions.Count(l => l.Status is ClassSessionStatus.Cancelled or ClassSessionStatus.CancelledNoshow);
            var noShows = tClassSessions.Count(l => l.Status == ClassSessionStatus.NoShow);
            var denom = completed + cancelled + noShows;
            decimal? rate = denom == 0 ? null : Math.Round((decimal)completed / denom * 100, 1);
            var revenue = tClassSessions
                .Where(l => l.Status == ClassSessionStatus.Completed && l.Issettled == true)
                .Sum(l => l.Lessonprice ?? 0);

            return new TutorPerformanceItem
            {
                TutorId = t.Tutorid ?? "",
                FullName = t.UserFullName ?? "",
                AvatarUrl = t.UserAvatar,
                AverageRating = t.Averagerating.HasValue ? Math.Round((decimal)t.Averagerating.Value, 2) : null,
                TotalFeedbacks = feedbacks.Count(f => f.Touserid == t.Tutorid),
                ClassSessionsCompleted = completed,
                ClassSessionsCancelled = cancelled,
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

        var topByClassSessions = tutorItems
            .OrderByDescending(t => t.ClassSessionsCompleted)
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
            FilterFrom = fromUtc,
            FilterTo = toUtc,
            PlatformAverageRating = platformAvgRating,
            PlatformAvgCompletionRate = platformAvgCompletion,
            TopByRating = topByRating,
            TopByClassSessionsCompleted = topByClassSessions,
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

    // ─── API 5: GET /api/admin/dashboard/summary ─────────────────────────────

    public async Task<AdminDashboardSummaryResponse> GetSummaryAsync(
        DateTimeOffset? from,
        DateTimeOffset? to,
        string timezone = "Asia/Ho_Chi_Minh",
        CancellationToken ct = default)
    {
        var nowUtc = TimeZoneHelper.UtcNow;
        var toUtc   = to?.UtcDateTime   ?? nowUtc;
        var fromUtc = from?.UtcDateTime ?? toUtc.AddDays(-30);

        // Kỳ liền trước (dùng cho % thay đổi) không còn tính ở đây: AdminRevenueAnalyticsService
        // đã trả sẵn cả cặp hiện tại/kỳ trước theo đúng quy ước "cùng độ dài, liền kề".

        logger.LogInformation(
            "AdminDashboardService.GetSummaryAsync from={From} to={To} tz={Tz}",
            fromUtc, toUtc, timezone);

        // Sequential fetch — EF Core DbContext is not thread-safe
        var bookings = await context.Bookings
            .AsNoTracking()
            .Select(b => new { b.Status, b.Finalprice, b.Platformfee, b.Createdat })
            .ToListAsync(ct);

        var withdrawals = await context.Withdrawalrequests
            .AsNoTracking()
            .Select(w => new { w.Status })
            .ToListAsync(ct);

        var disputes = await context.Disputes
            .AsNoTracking()
            .Select(d => new { d.Status })
            .ToListAsync(ct);

        var tutorProfiles = await context.Tutorprofiles
            .AsNoTracking()
            .Select(t => new { t.Profilestatus })
            .ToListAsync(ct);

        var pendingCertificatesCount = await context.Tutorcertificates
            .AsNoTracking()
            .CountAsync(c => c.Verificationstatus == CertificateStatus.PendingReview, ct);

        var unresolvedAlerts = await context.Systemalerts
            .AsNoTracking()
            .CountAsync(a => !a.Resolved, ct);

        // ── Tiền ─────────────────────────────────────────────────────────────
        //
        // KHÔNG tự tính ở đây nữa. Ba con số tiền của dashboard lấy nguyên từ
        // AdminRevenueAnalyticsService — đúng service đứng sau trang Báo cáo doanh thu.
        //
        // Trước đây dashboard có công thức riêng: lọc booking theo status rồi cộng
        // Finalprice / Platformfee. Công thức đó loại sạch mọi booking đã huỷ, kể cả khoá
        // phụ huynh đã trả tiền và đã học vài buổi — trong khi báo cáo tính cả nhóm đó theo
        // sổ ví. Hệ quả: cùng một khoảng 30 ngày, dashboard báo GMV 12.075.000 còn báo cáo
        // báo 17.902.500, và không có gì trong giao diện giải thích được vì sao.
        //
        // Gọi lại service kia thì tốn thêm một lượt tải bảng booking + class_sessions, đổi
        // lấy điều kiện quan trọng hơn nhiều: hai trang KHÔNG THỂ lệch nhau, vì chỉ còn đúng
        // một chỗ định nghĩa "tiền" trong hệ thống.
        var revenue = await revenueAnalytics.GetOverviewAsync(fromUtc, toUtc, ct);
        var s = revenue.Summary;

        // Kỳ trước dùng để tính % thay đổi: service kia đã tự tính sẵn với đúng quy ước
        // "kỳ liền trước cùng độ dài", nên ở đây không lặp lại phép chia khoảng nữa.
        static decimal? Change(decimal current, decimal previous) =>
            previous == 0 ? null : Math.Round((current - previous) / previous * 100, 1);

        var gmvChange        = Change(s.Gmv, s.GmvPrevious);
        var contractedChange = Change(s.CommissionSold, s.CommissionSoldPrevious);
        var recognisedChange = Change(s.RecognisedRevenue, s.RecognisedPrevious);

        // ── Booking counts ────────────────────────────────────────────────────
        var currentPeriodBookings = bookings
            .Where(b => b.Createdat >= fromUtc && b.Createdat <= toUtc)
            .ToList();
        var activeBookings    = bookings.Count(b => ActiveBookingStatuses.Contains(b.Status ?? ""));
        var newInPeriod       = currentPeriodBookings.Count;
        var completedInPeriod = currentPeriodBookings.Count(b => b.Status == BookingStatus.Completed);

        // ── Pending actions (real-time snapshot, no period filter) ────────────
        var tutorApprovals    = tutorProfiles.Count(t => t.Profilestatus == TutorProfileStatus.PendingApproval);
        var withdrawalReviews = withdrawals.Count(w =>
            w.Status == WithdrawalStatus.Pending ||
            w.Status == WithdrawalStatus.PendingReview);
        var openDisputes = disputes.Count(d =>
            d.Status is DisputeStatus.Pending or DisputeStatus.Investigating);
        var overdueCount = withdrawals.Count(w => w.Status == WithdrawalStatus.Delayed);
        var actionsTotal = tutorApprovals + pendingCertificatesCount + withdrawalReviews + openDisputes + unresolvedAlerts + overdueCount;

        return new AdminDashboardSummaryResponse
        {
            FilterFrom = fromUtc,
            FilterTo   = toUtc,
            BaseAmount = s.BaseAmount,
            Gmv = new MetricWithChange
            {
                Value         = s.Gmv,
                ChangePercent = gmvChange
            },
            // Doanh thu tạm tính: cố ý lấy CommissionSold (chỉ booking) chứ không lấy
            // ContractedRevenue (đã cộng thêm tiền bán gói AI). Lý do là để con số này TRÙNG
            // KHÍT với dòng "Doanh thu Tutora" trên thanh phân bổ của trang Báo cáo doanh thu —
            // doanh thu AI có tab riêng ở đó và không nằm trong thanh phân bổ.
            PlatformRevenue = new MetricWithChange
            {
                Value         = s.CommissionSold,
                ChangePercent = contractedChange
            },
            RecognisedRevenue = new MetricWithChange
            {
                Value         = s.RecognisedRevenue,
                ChangePercent = recognisedChange
            },
            Bookings = new SummaryBookings
            {
                Active            = activeBookings,
                NewInPeriod       = newInPeriod,
                CompletedInPeriod = completedInPeriod
            },
            PendingActions = new SummaryPendingActions
            {
                Total                = actionsTotal,
                TutorApprovals       = tutorApprovals,
                PendingCertificates  = pendingCertificatesCount,
                WithdrawalReviews    = withdrawalReviews,
                OpenDisputes         = openDisputes,
                UnresolvedAlerts     = unresolvedAlerts,
                OverdueCount         = overdueCount
            }
        };
    }

    // ─── API 4: GET /api/admin/dashboard/disputes ────────────────────────────

    public async Task<AdminDisputeStatsResponse> GetDisputeStatsAsync(
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken ct = default)
    {
        var nowUtc = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
        var toUtc = to?.UtcDateTime ?? nowUtc;
        var fromUtc = from?.UtcDateTime ?? toUtc.AddDays(-30);

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
                var vn = d.Createdat!.Value;
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
            FilterFrom = fromUtc,
            FilterTo = toUtc,
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

    // ─── API 5: GET /api/admin/dashboard/trends ───────────────────────────────

    public async Task<DashboardTrendResponse> GetTrendsAsync(DashboardTrendRequest request, CancellationToken ct = default)
    {
        // ── Resolve date range ────────────────────────────────────────────
        var tzOffset = ResolveTimezoneOffset(request.Timezone);
        var nowLocal = TimeZoneHelper.UtcNow.Add(tzOffset);

        var todayLocal = DateOnly.FromDateTime(nowLocal.Date);
        var toLocal    = TryParseDate(request.To,   todayLocal);
        var fromLocal  = TryParseDate(request.From, toLocal.AddDays(-29)); // default: last 30 days

        if (fromLocal > toLocal) fromLocal = toLocal.AddDays(-29);

        // Convert to UTC for DB query
        var fromUtc = fromLocal.ToDateTime(TimeOnly.MinValue).Subtract(tzOffset);
        var toUtc   = toLocal.ToDateTime(TimeOnly.MaxValue).Subtract(tzOffset);

        var totalDays = toLocal.DayNumber - fromLocal.DayNumber + 1;

        // ── Resolve bucket ────────────────────────────────────────────────
        var bucket = (request.Bucket?.Trim().ToLower()) switch
        {
            "day"   => "day",
            "week"  => "week",
            "month" => "month",
            _       => totalDays <= 31 ? "day" : totalDays <= 90 ? "week" : "month"
        };

        // ── Fetch data ────────────────────────────────────────────────────
        // Cùng COHORT với <see cref="IsInRevenueCohort"/> và với trang Báo cáo doanh thu: booking
        // đang chạy, HOẶC đã đóng sổ mà phụ huynh còn mất tiền thật. Điều kiện thứ hai phải tra
        // sổ ví nên không dịch được xuống SQL — lọc trong bộ nhớ sau khi nạp, thay vì viết lại
        // biểu thức. Hai trang mô tả cùng một khoản tiền nên KHÔNG được dùng hai bộ lọc khác nhau.
        var refundsByBooking = await LoadRefundsByBookingAsync(ct);

        var bookings = (await context.Bookings
                .AsNoTracking()
                .Where(b => b.Createdat >= fromUtc && b.Createdat <= toUtc)
                .Select(b => new
                {
                    b.Bookingid, b.Status, b.Createdat, b.Finalprice, b.Platformfee,
                    b.Depositpaidat, b.Remainingpaidat, b.Depositamount
                })
                .ToListAsync(ct))
            .Where(b => IsInRevenueCohort(
                b.Status, b.Depositpaidat, b.Remainingpaidat,
                b.Finalprice ?? 0, b.Depositamount ?? 0,
                refundsByBooking.TryGetValue(b.Bookingid, out var rf) ? rf : 0))
            .Select(b => new { b.Createdat, b.Finalprice, b.Platformfee })
            .ToList();

        var classSessions = await context.ClassSessions
            .AsNoTracking()
            .Where(l => l.Createdat >= fromUtc && l.Createdat <= toUtc)
            .Select(l => new { l.Createdat, l.Status })
            .ToListAsync(ct);

        // ── Build bucket labels ───────────────────────────────────────────
        var bucketLabels = BuildBucketLabels(fromLocal, toLocal, bucket);

        // ── Map to local time, group by bucket ───────────────────────────
        var financialTrend = bucketLabels.Select(bl => new FinancialTrendPoint
        {
            Label           = bl.Label,
            Gmv             = bookings
                .Where(b => b.Createdat.HasValue && BucketOf(b.Createdat.Value.Add(tzOffset), bucket) == bl.Key)
                .Sum(b => b.Finalprice ?? 0),
            PlatformRevenue = bookings
                .Where(b => b.Createdat.HasValue && BucketOf(b.Createdat.Value.Add(tzOffset), bucket) == bl.Key
                            && b.Platformfee.HasValue)
                .Sum(b => b.Platformfee ?? 0)
        }).ToList();

        var classSessionTrend = bucketLabels.Select(bl => new ClassSessionTrendPoint
        {
            Label     = bl.Label,
            Completed = classSessions.Count(l => l.Createdat.HasValue
                && BucketOf(l.Createdat.Value.Add(tzOffset), bucket) == bl.Key
                && l.Status == ClassSessionStatus.Completed),
            Cancelled = classSessions.Count(l => l.Createdat.HasValue
                && BucketOf(l.Createdat.Value.Add(tzOffset), bucket) == bl.Key
                && l.Status == ClassSessionStatus.Cancelled),
            NoShow    = classSessions.Count(l => l.Createdat.HasValue
                && BucketOf(l.Createdat.Value.Add(tzOffset), bucket) == bl.Key
                && (l.Status == ClassSessionStatus.NoShow || l.Status == ClassSessionStatus.CancelledNoshow))
        }).ToList();

        // ── Overall rates ─────────────────────────────────────────────────
        var totalCompleted  = classSessionTrend.Sum(x => x.Completed);
        var totalCancelled  = classSessionTrend.Sum(x => x.Cancelled);
        var totalNoShow     = classSessionTrend.Sum(x => x.NoShow);
        var totalClassSessions    = totalCompleted + totalCancelled + totalNoShow;

        var rates = totalClassSessions == 0
            ? new ClassSessionRates()
            : new ClassSessionRates
            {
                CompletionRate   = Math.Round((double)totalCompleted / totalClassSessions * 100, 1),
                CancellationRate = Math.Round((double)totalCancelled  / totalClassSessions * 100, 1),
                NoShowRate       = Math.Round((double)totalNoShow     / totalClassSessions * 100, 1)
            };

        return new DashboardTrendResponse
        {
            From           = fromLocal.ToString("yyyy-MM-dd"),
            To             = toLocal.ToString("yyyy-MM-dd"),
            Bucket         = bucket,
            FinancialTrend = financialTrend,
            ClassSessionTrend    = classSessionTrend,
            ClassSessionRates    = rates
        };
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static TimeSpan ResolveTimezoneOffset(string? timezone)
    {
        if (string.IsNullOrWhiteSpace(timezone)) return TimeSpan.FromHours(7);
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(
                // Windows ID fallback for IANA names
                timezone == "Asia/Ho_Chi_Minh" ? "SE Asia Standard Time" : timezone);
            return tz.BaseUtcOffset;
        }
        catch
        {
            return TimeSpan.FromHours(7); // fallback UTC+7
        }
    }

    private static DateOnly TryParseDate(string? value, DateOnly fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        return DateOnly.TryParseExact(value, "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var d) ? d : fallback;
    }

    private static string BucketOf(DateTime localDt, string bucket) => bucket switch
    {
        "week"  => ISOWeekKey(localDt),
        "month" => localDt.ToString("yyyy-MM"),
        _       => localDt.ToString("yyyy-MM-dd")
    };

    private static string ISOWeekKey(DateTime dt)
    {
        // ISO week: Monday-based, yields key like "2026-W25"
        var dow = (int)dt.DayOfWeek;
        var monday = dt.AddDays(-(dow == 0 ? 6 : dow - 1)).Date;
        return monday.ToString("yyyy-MM-dd");
    }

    private record BucketLabel(string Key, string Label);

    private static List<BucketLabel> BuildBucketLabels(DateOnly from, DateOnly to, string bucket)
    {
        var labels = new List<BucketLabel>();

        if (bucket == "day")
        {
            for (var d = from; d <= to; d = d.AddDays(1))
                labels.Add(new BucketLabel(d.ToString("yyyy-MM-dd"), d.ToString("dd/MM")));
        }
        else if (bucket == "week")
        {
            // Walk by Monday of each ISO week
            var cursor = from.ToDateTime(TimeOnly.MinValue);
            var dow = (int)cursor.DayOfWeek;
            cursor = cursor.AddDays(-(dow == 0 ? 6 : dow - 1)); // rewind to Monday

            var todt = to.ToDateTime(TimeOnly.MinValue);
            while (cursor <= todt)
            {
                var weekEnd = cursor.AddDays(6);
                var key   = cursor.ToString("yyyy-MM-dd");
                var label = $"{cursor:dd/MM} - {weekEnd:dd/MM}";
                labels.Add(new BucketLabel(key, label));
                cursor = cursor.AddDays(7);
            }
        }
        else // month
        {
            var cursor = new DateOnly(from.Year, from.Month, 1);
            while (cursor <= to)
            {
                var key   = cursor.ToString("yyyy-MM");
                var label = $"T{cursor.Month:D2}/{cursor.Year}";
                labels.Add(new BucketLabel(key, label));
                cursor = cursor.AddMonths(1);
            }
        }

        return labels;
    }
}
