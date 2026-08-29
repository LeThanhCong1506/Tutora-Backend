namespace MV.DomainLayer.Constants;

/// <summary>
/// No-show action type constants. Values match the `noshowaction` column in the `classSessions` table.
/// </summary>
public static class NoShowActionTypes
{
    /// <summary>Full refund, session not counted.</summary>
    public const string FreeSession = "free_session";

    /// <summary>Schedule a makeup classSession.</summary>
    public const string Makeup = "makeup";

    /// <summary>Cancel booking and refund remaining sessions.</summary>
    public const string ChangeTutor = "change_tutor";

    /// <summary>
    /// Buổi không ghi nhận ai vào lớp, đã báo cả hai bên và hết hạn phản hồi mà không ai có ý
    /// kiến — hệ thống tự đưa về luồng xác nhận bình thường. Đánh dấu riêng để thống kê và đánh
    /// giá gia sư không coi nó như một buổi dạy thật (buổi thật luôn có class_session_reports).
    /// Giá trị phải ≤ 30 ký tự — giới hạn của cột no_show_action.
    /// </summary>
    public const string AutoNoAttendance = "auto_no_attendance";

    public static readonly string[] All = { FreeSession, Makeup, ChangeTutor, AutoNoAttendance };
}
