using MV.DomainLayer.Constants;
using MV.DomainLayer.Helpers;
using static MV.DomainLayer.Constants.ClassSessionStatus;

namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// Rich classSession detail — returned by all tutor action endpoints and parent classSession queries.
/// Callers: <c>GetTutorClassSessionDetailAsync</c>, <c>CheckInAsync</c>, <c>CheckOutAsync</c>,
/// <c>SubmitReportAsync</c>, <c>ReportTutorNoShowAsync</c>, <c>CreateMakeupClassSessionAsync</c>,
/// <c>GetClassSessionDetailAsync</c> (parent/tutor via ParentClassSessionService).
/// Includes attendance, report content, makeup info, and computed time-check helpers.
/// All DateTime fields are Vietnam time (UTC+7).
/// </summary>
public class ClassSessionDetailResponse
{
    // Tất cả comparison dùng giờ VN vì ScheduledStart/ConfirmDeadline đã là giờ VN (now using VietnamTimeHelper)

    public int ClassSessionId { get; set; }
    public int? BookingId { get; set; }

    // Schedule info
    public DateTime ScheduledStart { get; set; }
    public DateTime ScheduledEnd { get; set; }
    public DateTime? RealStart { get; set; }
    public DateTime? RealEnd { get; set; }

    // Attendance
    public DateTime? CheckInTime { get; set; }
    public DateTime? CheckOutTime { get; set; }
    public bool? IsTutorPresent { get; set; }
    public bool? IsStudentPresent { get; set; }
    public string? AttendanceNote { get; set; }

    // Status
    public string? Status { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? ConfirmDeadline { get; set; }
    public DateTime? ParentAckAt { get; set; }
    public bool? IsSettled { get; set; }

    // Content
    public string? ClassSessionContent { get; set; }
    public string? Homework { get; set; }
    public string? TutorNotes { get; set; }
    public string? MeetingLink { get; set; }

    // Price info
    public decimal? ClassSessionPrice { get; set; }

    // Makeup info
    public bool? IsMakeup { get; set; }
    public int? OriginalClassSessionId { get; set; }
    public string? NoShowAction { get; set; }

    // Related entities
    public ClassSessionStudentResponse? Student { get; set; }
    public ClassSessionTutorResponse? Tutor { get; set; }
    public ClassSessionSubjectResponse? Subject { get; set; }
    public ClassSessionReportResponse? Report { get; set; }

    // Time calculations (so sánh với giờ VN vì ScheduledStart/ConfirmDeadline đã là giờ VN)
    public TimeSpan? TimeUntilStart => ScheduledStart > TimeZoneHelper.UtcNow
        ? ScheduledStart - TimeZoneHelper.UtcNow
        : null;

    public TimeSpan? TimeRemainingToConfirm => ConfirmDeadline.HasValue && ConfirmDeadline > TimeZoneHelper.UtcNow
        ? ConfirmDeadline - TimeZoneHelper.UtcNow
        : null;

    public bool CanCheckIn => Status == Scheduled &&
        Math.Abs((TimeZoneHelper.UtcNow - ScheduledStart).TotalMinutes) <= 15;

    public bool CanSubmitReport => Status == InProgress && CheckInTime.HasValue;
}

public class ClassSessionStudentResponse
{
    public string? StudentId { get; set; }
    public string? FullName { get; set; }
    public string? School { get; set; }
    public string? GradeLevel { get; set; }
    public string? AvatarUrl { get; set; }
}

public class ClassSessionTutorResponse
{
    public string? TutorId { get; set; }
    public string? FullName { get; set; }
    public string? AvatarUrl { get; set; }
    public double? AverageRating { get; set; }
}

public class ClassSessionSubjectResponse
{
    public int SubjectId { get; set; }
    public string? SubjectName { get; set; }
}

public class ClassSessionReportResponse
{
    public int ReportId { get; set; }
    public string? ContentCovered { get; set; }
    public string? HomeworkAssigned { get; set; }
    public int? StudentPerformanceRating { get; set; }
    public List<string>? Attachments { get; set; }
    public DateTime? CreatedAt { get; set; }
}
