namespace MV.ApplicationLayer.Services.Agora;

/// <summary>
/// Nguồn CHÂN LÝ DUY NHẤT cho tên channel Agora RTC của một buổi học.
///
/// Cả client (tutor/student/parent join video call) LẪN Cloud Recording recorder PHẢI join
/// đúng cùng một channel — nếu lệch, recorder sẽ ghi một phòng trống (0 file). Trước đây
/// recorder tự suy channel = classSessionId trong khi client dùng "booking-{id}", gây ghi
/// hình rỗng. Mọi nơi cần tên channel phải gọi <see cref="Resolve"/> để không bao giờ lệch nữa.
/// </summary>
public static class AgoraChannel
{
    /// <summary>
    /// Channel dùng chung theo booking: <c>booking-{bookingId}</c> — mọi buổi của cùng booking
    /// chia sẻ một phòng ("một meet link"). Fallback theo classSessionId khi buổi chưa gắn booking.
    /// </summary>
    public static string Resolve(int classSessionId, int? bookingId)
        => bookingId.HasValue ? $"booking-{bookingId.Value}" : classSessionId.ToString();
}
