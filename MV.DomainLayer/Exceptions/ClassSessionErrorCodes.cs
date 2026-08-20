namespace MV.DomainLayer.Exceptions;

/// <summary>
/// Error codes for classSession operations
/// </summary>
public static class ClassSessionErrorCodes
{
    // Existing error codes
    public const string ClassSessionNotFound = "LESSON_NOT_FOUND";
    public const string ClassSessionAlreadyExists = "LESSON_ALREADY_EXISTS";
    public const string InvalidSchedule = "INVALID_SCHEDULE";
    public const string ScheduleConflict = "SCHEDULE_CONFLICT";
    public const string InvalidClassSessionDate = "INVALID_LESSON_DATE";

    // M3 error codes
    public const string InvalidClassSessionStatus = "INVALID_LESSON_STATUS";
    public const string CheckInTooEarly = "CHECK_IN_TOO_EARLY";
    public const string NotCheckedIn = "NOT_CHECKED_IN";
    public const string NotCheckedOut = "NOT_CHECKED_OUT";
    public const string ReportAlreadySubmitted = "REPORT_ALREADY_SUBMITTED";
    public const string InvalidNoShowAction = "INVALID_NO_SHOW_ACTION";
    public const string MakeupTimeRequired = "MAKEUP_TIME_REQUIRED";
    public const string ClassSessionAlreadyConfirmed = "LESSON_ALREADY_CONFIRMED";
    public const string ConfirmDeadlinePassed = "CONFIRM_DEADLINE_PASSED";
    public const string UnauthorizedAccess = "UNAUTHORIZED_ACCESS";
    public const string DisputeAlreadyExists = "DISPUTE_ALREADY_EXISTS";

    // Buổi học bị ngắt giữa chừng / buổi phụ
    public const string AlreadyContinuationSession = "ALREADY_CONTINUATION_SESSION";
    public const string InterruptionThresholdNotMet = "INTERRUPTION_THRESHOLD_NOT_MET";
}
