using Microsoft.AspNetCore.Http;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces;

public interface ILessonService
{
    // ── Student lesson access ──────────────────────────────────────────────

    /// <summary>
    /// Paged lesson list from the student's perspective, optionally filtered by status.
    /// </summary>
    Task<(IReadOnlyList<StudentLessonSummaryResponse> Items, int TotalCount)> GetStudentLessonsAsync(string studentProfileId, int page, int pageSize, string? status);

    /// <summary>
    /// Full lesson detail for a student — includes meeting link and report.
    /// </summary>
    Task<StudentLessonDetailResponse?> GetStudentLessonDetailAsync(int lessonId, string studentProfileId);

    /// <summary>
    /// Unpaged list of upcoming/pending lessons for a student (used for reminders).
    /// </summary>
    Task<IReadOnlyList<StudentLessonSummaryResponse>> GetStudentPendingLessonsAsync(string studentProfileId);

    // ── Core lesson management ─────────────────────────────────────────────

    /// <summary>
    /// Background: auto-generate all lesson records for a booking after tutor acceptance.
    /// </summary>
    Task AutoCreateLessonsAsync(int bookingId, CancellationToken ct = default);

    /// <summary>
    /// Paged lesson list from the tutor's perspective, filterable by date and status.
    /// </summary>
    Task<PagedList<LessonResponse>> GetTutorLessonsAsync(string tutorId, int page, int pageSize, DateTime? fromDate, string? status);

    /// <summary>
    /// Paged lesson list from the parent's perspective, filterable by date and status.
    /// </summary>
    Task<PagedList<LessonResponse>> GetParentLessonsAsync(string parentId, int page, int pageSize, DateTime? fromDate, string? status);

    /// <summary>
    /// Single lesson by id — ownership-checked against the calling user.
    /// </summary>
    Task<LessonResponse?> GetLessonByIdAsync(int lessonId, string userId, bool isParent);

    // ── Calendar & Dashboard ───────────────────────────────────────────────

    /// <summary>
    /// Tutor calendar view — returns daily lesson summaries for a date range.
    /// </summary>
    Task<List<CalendarDayResponse>> GetTutorCalendarAsync(string tutorId, DateTime startDate, DateTime endDate);

    /// <summary>
    /// Student calendar view — returns daily lesson summaries for a date range.
    /// Resolves student profile from either studentId or linkedUserId.
    /// </summary>
    Task<List<CalendarDayResponse>> GetStudentCalendarAsync(string studentUserId, DateTime startDate, DateTime endDate);

    /// <summary>
    /// Aggregate dashboard stats for a tutor: total lessons, earnings, upcoming count.
    /// </summary>
    Task<TutorDashboardStatsResponse> GetTutorDashboardStatsAsync(string tutorId);

    /// <summary>
    /// Full lesson detail for a tutor — includes check-in/out timestamps and report.
    /// </summary>
    Task<LessonDetailResponse?> GetTutorLessonDetailAsync(int lessonId, string tutorId);

    // ── Check-in / Check-out / Report ──────────────────────────────────────

    /// <summary>
    /// Tutor marks the lesson as started (check-in) with attendance info.
    /// </summary>
    Task<LessonDetailResponse> CheckInAsync(int lessonId, string tutorId, CheckInRequest request);

    /// <summary>
    /// Tutor marks the lesson as ended (check-out) with duration data.
    /// </summary>
    Task<LessonDetailResponse> CheckOutAsync(int lessonId, string tutorId, CheckOutRequest request);

    /// <summary>
    /// Tutor submits a post-lesson report (homework, notes, rating).
    /// </summary>
    Task<LessonDetailResponse> SubmitReportAsync(int lessonId, string tutorId, SubmitReportRequest request);

    /// <summary>
    /// Upload a file attachment to a lesson (e.g. homework document).
    /// Returns the public URL of the uploaded file.
    /// </summary>
    Task<string> UploadAttachmentAsync(int lessonId, string tutorId, IFormFile file);

    // ── No-show handling ───────────────────────────────────────────────────

    /// <summary>
    /// Parent reports that the tutor did not show up for a scheduled lesson.
    /// </summary>
    Task<LessonDetailResponse> ReportTutorNoShowAsync(int lessonId, string parentId);

    /// <summary>
    /// Parent selects a resolution action after a tutor no-show
    /// (free session, makeup lesson, or change tutor).
    /// </summary>
    Task<NoShowActionResultResponse> ProcessNoShowActionAsync(int lessonId, string parentId, NoShowActionRequest request);

    /// <summary>
    /// Tutor creates a makeup lesson to compensate for a previously missed session.
    /// </summary>
    Task<LessonDetailResponse> CreateMakeupLessonAsync(int originalLessonId, DateTime newScheduledStart, string tutorId);

    /// <summary>
    /// Cancel a single scheduled lesson — only the lesson owner (tutor) or the
    /// booking's parent may cancel. Only lessons with status <c>scheduled</c> can be cancelled.
    /// Sends a notification to the other party.
    /// </summary>
    Task<LessonResponse> CancelLessonAsync(int lessonId, string userId, string userRole, string? reason = null);

    /// <summary>
    /// Gán Tencent RTC RoomId (= lessonId) cho tất cả buổi học online/hybrid sắp tới
    /// chưa có RoomId, rồi gửi thông báo chat cho parent của từng booking.
    /// Fire-and-forget safe — never throws; logs warnings on individual failures.
    /// </summary>
    Task RefreshMeetLinksForTutorAsync(string tutorId);
}
