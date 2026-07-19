namespace MV.ApplicationLayer.Services.Agora;

/// <summary>
/// Nguồn chân lý duy nhất cho tên channel Agora RTC của một buổi học.
///
/// Channel dùng chung theo booking: <c>booking-{bookingId}</c> — mọi buổi của cùng một
/// booking chia sẻ một phòng ("một meet link"). Fallback theo classSessionId cho dữ liệu
/// cũ chưa gắn booking. Cloud Recording PHẢI dùng đúng công thức này để recorder join
/// đúng phòng mà gia sư/học viên đang học — join sai channel là ghi ra video trống.
/// </summary>
public static class AgoraChannelName
{
    public static string ForSession(int classSessionId, int? bookingId)
        => bookingId.HasValue ? $"booking-{bookingId.Value}" : classSessionId.ToString();
}
