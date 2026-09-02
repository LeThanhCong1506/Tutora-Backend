namespace MV.DomainLayer.Constants;

/// <summary>
/// No-show action type constants. Values match the `noshowaction` column in the `classSessions` table.
///
/// Luồng để phụ huynh tự chọn cách xử lý sau khi gia sư vắng (free_session / makeup / change_tutor)
/// đã được gỡ bỏ: mọi ca vắng mặt nay đi qua khiếu nại để Admin/Staff phân xử. Ba giá trị đó vẫn
/// tồn tại trong DB ở các bản ghi cũ nên KHÔNG khai báo lại ở đây, tránh code mới sinh thêm.
/// </summary>
public static class NoShowActionTypes
{
    /// <summary>
    /// Buổi không ghi nhận ai vào lớp, đã báo cả hai bên và hết hạn phản hồi mà không ai có ý
    /// kiến — hệ thống tự đưa về luồng xác nhận bình thường. Đánh dấu riêng để thống kê và đánh
    /// giá gia sư không coi nó như một buổi dạy thật (buổi thật luôn có class_session_reports).
    /// Giá trị phải ≤ 30 ký tự — giới hạn của cột no_show_action.
    /// </summary>
    public const string AutoNoAttendance = "auto_no_attendance";

    public static readonly string[] All = { AutoNoAttendance };
}
