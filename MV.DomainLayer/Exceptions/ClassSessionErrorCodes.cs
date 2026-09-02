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
    public const string TooEarlyToReportNoShow = "TOO_EARLY_TO_REPORT_NO_SHOW";
    public const string InvalidNoShowAction = "INVALID_NO_SHOW_ACTION";
    public const string MakeupTimeRequired = "MAKEUP_TIME_REQUIRED";
    public const string ClassSessionAlreadyConfirmed = "LESSON_ALREADY_CONFIRMED";
    public const string ConfirmDeadlinePassed = "CONFIRM_DEADLINE_PASSED";
    public const string UnauthorizedAccess = "UNAUTHORIZED_ACCESS";
    public const string DisputeAlreadyExists = "DISPUTE_ALREADY_EXISTS";

    // Buổi học bị ngắt giữa chừng / buổi phụ
    public const string AlreadyContinuationSession = "ALREADY_CONTINUATION_SESSION";
    public const string InterruptionThresholdNotMet = "INTERRUPTION_THRESHOLD_NOT_MET";
    // Đã dạy thật bằng/vượt thời lượng đăng ký của buổi — không còn phần "thiếu" để tạo buổi phụ.
    public const string InterruptionNoRemainingTime = "INTERRUPTION_NO_REMAINING_TIME";
    // Buổi học lại do admin hoà giải tranh chấp — cùng lý do chặn với buổi phụ (tránh 1 buổi đã
    // được admin can thiệp 1 lần lại tiếp tục sinh thêm buổi phụ qua chính cơ chế Báo ngắt).
    public const string AlreadyRelearnSession = "ALREADY_RELEARN_SESSION";

    // Chuỗi buổi (gốc + mọi buổi phụ/bù/học lại) đã chạm MaxRelearnSessionsPerChain — không tạo
    // thêm buổi bù no-show được nữa, dù đường này khác với đường hoà giải dispute ở trên.
    public const string SessionChainLimitReached = "SESSION_CHAIN_LIMIT_REACHED";

    // Buổi phụ (Iscontinuation) của buổi gốc đang Interrupted đã tự settle độc lập (Completed) rồi —
    // không cho nộp báo cáo buổi gốc nữa, tránh double-settle cùng 1 buổi học logic.
    public const string ContinuationAlreadySettled = "CONTINUATION_ALREADY_SETTLED";

    // Buổi phụ đã check-in thật (InProgress) — hai bên đã có mặt và đang dạy dở — nên không cho
    // "lách" qua bằng cách nộp báo cáo trên buổi gốc để hưởng full giá mà bỏ qua phần đã dạy dở
    // đó (xem SubmitReportAsync). Chỉ buổi phụ còn Scheduled (chưa ai vào) mới được tự huỷ ngầm.
    public const string ContinuationInProgress = "CONTINUATION_IN_PROGRESS";
}
