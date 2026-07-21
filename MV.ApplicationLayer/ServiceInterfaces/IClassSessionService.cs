using Microsoft.AspNetCore.Http;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces;

public interface IClassSessionService
{
    // ── Student classSession access ──────────────────────────────────────────────

    /// <summary>
    /// Paged classSession list from the student's perspective, optionally filtered by status.
    /// </summary>
    Task<(IReadOnlyList<StudentClassSessionSummaryResponse> Items, int TotalCount)> GetStudentClassSessionsAsync(string studentProfileId, int page, int pageSize, string? status);

    /// <summary>
    /// Full classSession detail for a student — includes meeting link and report.
    /// </summary>
    Task<StudentClassSessionDetailResponse?> GetStudentClassSessionDetailAsync(int classSessionId, string studentProfileId);

    /// <summary>
    /// Unpaged list of upcoming/pending classSessions for a student (used for reminders).
    /// </summary>
    Task<IReadOnlyList<StudentClassSessionSummaryResponse>> GetStudentPendingClassSessionsAsync(string studentProfileId);

    // ── Core classSession management ─────────────────────────────────────────────

    /// <summary>
    /// Background: auto-generate all classSession records for a booking after tutor acceptance.
    /// </summary>
    Task AutoCreateClassSessionsAsync(int bookingId, CancellationToken ct = default);

    /// <summary>
    /// Paged classSession list from the tutor's perspective, filterable by date and status.
    /// </summary>
    Task<PagedList<ClassSessionResponse>> GetTutorClassSessionsAsync(string tutorId, int page, int pageSize, DateTime? fromDate, string? status);

    /// <summary>
    /// Paged "class" list (one row per booking) for the tutor "Quản lý lớp học" screen. Grouping,
    /// progress and derived status are computed server-side. <paramref name="status"/> is the derived
    /// class status; <paramref name="search"/> matches subject or student name.
    /// </summary>
    Task<TutorClassListResponse> GetTutorClassesAsync(string tutorId, int page, int pageSize, string? status, string? search);

    /// <summary>
    /// Paged classSession list from the parent's perspective, filterable by date and status.
    /// </summary>
    Task<PagedList<ClassSessionResponse>> GetParentClassSessionsAsync(string parentId, int page, int pageSize, DateTime? fromDate, string? status);

    /// <summary>
    /// Single classSession by id — ownership-checked against the calling user.
    /// </summary>
    Task<ClassSessionResponse?> GetClassSessionByIdAsync(int classSessionId, string userId, bool isParent);

    // ── Calendar & Dashboard ───────────────────────────────────────────────

    /// <summary>
    /// Tutor calendar view — returns daily classSession summaries for a date range.
    /// </summary>
    Task<List<CalendarDayResponse>> GetTutorCalendarAsync(string tutorId, DateTime startDate, DateTime endDate);

    /// <summary>
    /// Student calendar view — returns daily classSession summaries for a date range.
    /// Resolves student profile from either studentId or linkedUserId.
    /// </summary>
    Task<List<CalendarDayResponse>> GetStudentCalendarAsync(string studentUserId, DateTime startDate, DateTime endDate);

    /// <summary>
    /// Aggregate dashboard stats for a tutor: total classSessions, earnings, upcoming count.
    /// </summary>
    Task<TutorDashboardStatsResponse> GetTutorDashboardStatsAsync(string tutorId);

    /// <summary>
    /// Full classSession detail for a tutor — includes check-in/out timestamps and report.
    /// </summary>
    Task<ClassSessionDetailResponse?> GetTutorClassSessionDetailAsync(int classSessionId, string tutorId);

    // ── Check-in / Check-out / Report ──────────────────────────────────────
    // Check-in không còn thủ công: xem TryAutoCheckInAsync (presence-driven) bên dưới.

    /// <summary>
    /// Tutor kết thúc buổi học (check-out): ghi giờ ra và đóng phòng. Giữ trạng thái
    /// <c>in_progress</c> để gia sư vẫn gửi được báo cáo sau đó.
    /// </summary>
    Task<ClassSessionDetailResponse> CheckOutAsync(int classSessionId, string tutorId, CheckOutRequest request);

    /// <summary>
    /// Tutor submits a post-classSession report (homework, notes, rating).
    /// </summary>
    Task<ClassSessionDetailResponse> SubmitReportAsync(int classSessionId, string tutorId, SubmitReportRequest request);

    /// <summary>
    /// Presence-driven auto check-in: khi cả gia sư và học viên (hoặc phụ huynh thay thế)
    /// cùng có mặt trong phòng của buổi <paramref name="classSessionId"/>, tự chuyển buổi từ
    /// <c>scheduled</c> sang <c>in_progress</c> và ghi check-in. Một người có mặt không đủ.
    /// An toàn khi gọi lặp (idempotent) — chỉ đổi trạng thái đúng một lần.
    /// </summary>
    Task<SessionPresenceStatus> TryAutoCheckInAsync(int classSessionId);

    /// <summary>
    /// Upload a file attachment to a classSession (e.g. homework document).
    /// Returns the public URL of the uploaded file.
    /// </summary>
    Task<string> UploadAttachmentAsync(int classSessionId, string tutorId, IFormFile file);

    /// <summary>
    /// True nếu buổi học là buổi TIẾP THEO của booking nhưng phụ huynh CHƯA thanh toán
    /// đợt 2 (các buổi còn lại). Buổi đầu luôn false. Dùng để chặn cấp token Agora và
    /// để FE khóa link buổi chưa được thanh toán.
    /// </summary>
    Task<bool> IsSessionBlockedByRemainingPaymentAsync(int classSessionId);

    // ── No-show handling ───────────────────────────────────────────────────

    /// <summary>
    /// Parent (or self-managed student) reports that the tutor did not show up for a scheduled classSession.
    /// </summary>
    Task<ClassSessionDetailResponse> ReportTutorNoShowAsync(int classSessionId, string userId, string role);

    /// <summary>
    /// Parent (or self-managed student) selects a resolution action after a tutor no-show
    /// (free session, makeup classSession, or change tutor).
    /// </summary>
    Task<NoShowActionResultResponse> ProcessNoShowActionAsync(int classSessionId, string userId, string role, NoShowActionRequest request);

    /// <summary>
    /// Tutor creates a makeup classSession to compensate for a previously missed session.
    /// </summary>
    Task<ClassSessionDetailResponse> CreateMakeupClassSessionAsync(int originalClassSessionId, DateTime newScheduledStart, string tutorId);

    /// <summary>
    /// Always rejects: single-classSession cancel is unsupported because escrow release
    /// requires cancelling the full booking. Kept so the ownership check runs before the 400.
    /// </summary>
    Task<ClassSessionResponse> CancelClassSessionAsync(int classSessionId, string userId, string userRole, string? reason = null);

    /// <summary>
    /// Gán Agora RTC channel (= classSessionId) cho tất cả buổi học online/hybrid sắp tới
    /// chưa có channel, rồi gửi thông báo chat cho parent của từng booking.
    /// Fire-and-forget safe — never throws; logs warnings on individual failures.
    /// </summary>
    Task RefreshMeetLinksForTutorAsync(string tutorId);
}
