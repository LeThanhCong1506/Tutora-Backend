namespace MV.DomainLayer.Constants;

/// <summary>
/// ClassSession status constants
/// </summary>
public static class ClassSessionStatus
{
    public const string Scheduled = "scheduled";
    public const string Reserved = "reserved";
    public const string InProgress = "in_progress";
    public const string PendingConfirmation = "pending_confirmation";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
    public const string Disputed = "disputed";
    public const string NoShow = "no_show";
    public const string CancelledNoshow = "cancelled_noshow";

    /// <summary>Buổi gốc bị ngắt giữa chừng vì sự cố đột xuất — trạng thái cụt, không tự chuyển
    /// sang pending_confirmation/completed; chỉ buổi phụ (Iscontinuation) hoặc job tự đóng quá hạn
    /// mới đưa buổi này tới trạng thái cuối.</summary>
    public const string Interrupted = "interrupted";
}
