namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// Toàn bộ chat của cặp gia sư - phụ huynh/học sinh kèm mốc thời gian của booking (và buổi học)
/// đang bị tranh chấp. Kênh chat là per cặp người chứ không per booking, nên không cắt được lịch
/// sử theo booking; thay vào đó trả mốc để admin biết đoạn nào thuộc phạm vi tranh chấp.
/// </summary>
public class DisputeChatHistoryResponse
{
    public int ChannelId { get; set; }

    public int? DisputedBookingId { get; set; }

    /// <summary>Thời điểm tạo booking đang tranh chấp.</summary>
    public DateTime? BookingWindowStart { get; set; }

    /// <summary>Kết thúc buổi học cuối của booking. Null = booking còn đang chạy (cửa sổ mở).</summary>
    public DateTime? BookingWindowEnd { get; set; }

    public int? DisputedClassSessionId { get; set; }

    public DateTime? SessionWindowStart { get; set; }

    public DateTime? SessionWindowEnd { get; set; }

    public List<DisputeChatMessageResponse> Messages { get; set; } = new();
}

/// <summary>Message kèm cờ cho biết nó rơi vào phạm vi nào so với tranh chấp.</summary>
public class DisputeChatMessageResponse : ChatMessageResponse
{
    /// <summary>Gửi trước khi booking đang tranh chấp được tạo (giai đoạn thương lượng).</summary>
    public bool IsBeforeBooking { get; set; }

    /// <summary>Nằm trong khoảng thời gian của booking đang tranh chấp.</summary>
    public bool IsWithinDisputedBooking { get; set; }

    /// <summary>Nằm trong khoảng buổi học cụ thể bị tranh chấp (nếu dispute gắn với 1 buổi).</summary>
    public bool IsWithinDisputedSession { get; set; }
}
