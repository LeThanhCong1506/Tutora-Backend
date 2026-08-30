namespace MV.ApplicationLayer.Helpers;

/// <summary>
/// Ràng buộc thời gian giữa lúc ĐẶT, lúc gia sư PHẢN HỒI, và lúc buổi học BẮT ĐẦU.
///
/// Bug gốc: hạn phản hồi trước đây luôn là "đóng cọc + 24h", hoàn toàn độc lập với giờ học. Phụ
/// huynh đặt 20:00 cho buổi 21:00 cùng ngày thì gia sư vẫn có tới 20:00 HÔM SAU để bấm chấp nhận
/// — tức duyệt một buổi đã trôi qua 23 tiếng. Buổi đó rồi rơi vào AbandonedSessionJob và được
/// auto-complete, nghĩa là gia sư được trả tiền cho buổi chưa từng có cơ hội diễn ra.
///
/// Chỉ tăng thời gian báo trước KHÔNG sửa được: hễ khoảng cách tới buổi học còn nhỏ hơn hạn phản
/// hồi thì lỗ hổng còn nguyên, chỉ thu hẹp lại. Thứ thực sự đóng nó là chặn trên hạn phản hồi
/// bằng chính giờ học (<see cref="ResolveResponseDeadline"/>).
///
/// Hai hằng số dưới đây cố tình gom về một chỗ: nếu sau này chuyển sang mô hình "mỗi gia sư tự
/// đặt thời gian báo trước" (như Advance Notice của Preply), chỉ cần thay nguồn giá trị chứ không
/// phải viết lại logic.
/// </summary>
public static class BookingLeadTimePolicy
{
    /// <summary>Buổi học phải cách thời điểm đặt ít nhất chừng này — gia sư cần thời gian sắp xếp.</summary>
    public const int MinimumLeadHours = 24;

    /// <summary>
    /// Gia sư phải chốt trước giờ học ít nhất chừng này, để phụ huynh còn kịp biết và chuẩn bị
    /// (hoặc kịp tìm phương án khác nếu bị từ chối).
    /// </summary>
    public const int ResponseBufferHours = 2;

    /// <summary>Tối đa gia sư được giữ một yêu cầu bao lâu, tính từ lúc phụ huynh đóng cọc.</summary>
    public const int MaxResponseHours = 24;

    /// <summary>
    /// Hạn phản hồi thật = sớm hơn giữa "đóng cọc + 24h" và "giờ buổi đầu − 2h".
    ///
    /// Neo vào buổi ĐẦU TIÊN vì đó là buổi duy nhất có nguy cơ trôi qua trước khi gia sư kịp trả
    /// lời; các buổi sau còn ở trạng thái reserved cho tới khi thanh toán phần còn lại.
    ///
    /// <paramref name="firstSessionStartUtc"/> null (booking không có buổi nào — dữ liệu bất
    /// thường) thì lùi về luật cũ 24h thay vì ném lỗi, để không chặn đường thanh toán.
    /// </summary>
    public static DateTime ResolveResponseDeadline(DateTime nowUtc, DateTime? firstSessionStartUtc)
    {
        var byMaxHold = nowUtc.AddHours(MaxResponseHours);
        if (!firstSessionStartUtc.HasValue) return byMaxHold;

        var bySessionStart = firstSessionStartUtc.Value.AddHours(-ResponseBufferHours);
        return bySessionStart < byMaxHold ? bySessionStart : byMaxHold;
    }

    /// <summary>Buổi học có đủ xa để được đặt không.</summary>
    public static bool IsFarEnoughToBook(DateTime nowUtc, DateTime sessionStartUtc)
        => sessionStartUtc >= nowUtc.AddHours(MinimumLeadHours);
}
