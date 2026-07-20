namespace MV.ApplicationLayer.Services.Agora;

/// <summary>
/// Nguồn chân lý duy nhất cho tên channel Agora RTC của một buổi học.
///
/// Mỗi class session dùng một channel riêng. Điều này giữ media room, device lease,
/// heartbeat và recording cùng một security scope; hai buổi thuộc cùng booking không thể
/// vô tình nhìn/nghe thấy nhau. Cloud Recording PHẢI dùng đúng công thức này để recorder
/// join đúng phòng mà gia sư/học viên đang học.
/// </summary>
public static class AgoraChannelName
{
    public static string ForSession(int classSessionId, int? bookingId)
    {
        // Keep bookingId in the signature for existing callers; channel identity is session-scoped.
        _ = bookingId;
        return classSessionId.ToString();
    }
}
