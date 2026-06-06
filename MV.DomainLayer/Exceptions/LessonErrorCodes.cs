namespace MV.DomainLayer.Exceptions;

/// <summary>
/// Error codes for lesson operations
/// </summary>
public static class LessonErrorCodes
{
    // Existing error codes
    public const string LessonNotFound = "LESSON_NOT_FOUND";
    public const string LessonAlreadyExists = "LESSON_ALREADY_EXISTS";
    public const string InvalidSchedule = "INVALID_SCHEDULE";
    public const string ScheduleConflict = "SCHEDULE_CONFLICT";
    public const string InvalidLessonDate = "INVALID_LESSON_DATE";

    // M3 error codes
    public const string InvalidLessonStatus = "INVALID_LESSON_STATUS";
    public const string CheckInTooEarly = "CHECK_IN_TOO_EARLY";
    public const string NotCheckedIn = "NOT_CHECKED_IN";
    public const string NotCheckedOut = "NOT_CHECKED_OUT";
    public const string ReportAlreadySubmitted = "REPORT_ALREADY_SUBMITTED";
    public const string TooEarlyToReportNoShow = "TOO_EARLY_TO_REPORT_NO_SHOW";
    public const string InvalidNoShowAction = "INVALID_NO_SHOW_ACTION";
    public const string MakeupTimeRequired = "MAKEUP_TIME_REQUIRED";
    public const string LessonAlreadyConfirmed = "LESSON_ALREADY_CONFIRMED";
    public const string ConfirmDeadlinePassed = "CONFIRM_DEADLINE_PASSED";
    public const string UnauthorizedAccess = "UNAUTHORIZED_ACCESS";
    public const string DisputeAlreadyExists = "DISPUTE_ALREADY_EXISTS";
}
