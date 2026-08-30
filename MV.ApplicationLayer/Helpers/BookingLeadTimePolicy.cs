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

    // ── Yêu cầu đặt lịch do HỌC SINH tạo, chờ phụ huynh xem và thanh toán ────────────────────

    /// <summary>Cửa sổ ngắn hơn chừng này thì phụ huynh không kịp thao tác — từ chối ngay lúc tạo.</summary>
    public const int MinimumParentWindowHours = 2;

    /// <summary>
    /// Buổi học phải cách chừng này thì yêu cầu do học sinh tạo mới khả thi: đủ để phụ huynh
    /// thanh toán trước mốc <see cref="MinimumLeadHours"/> mà vẫn còn ít nhất
    /// <see cref="MinimumParentWindowHours"/> để thao tác.
    /// </summary>
    public const int MinimumLeadHoursForStudentRequest = MinimumLeadHours + MinimumParentWindowHours;

    /// <summary>Phụ huynh không được giữ một yêu cầu quá lâu — học sinh và gia sư đều đang chờ.</summary>
    public const int ParentReviewHours = 24;

    /// <summary>
    /// Hạn để phụ huynh xem và thanh toán = sớm hơn giữa <c>lúc gửi + 24h</c> và
    /// <c>giờ buổi đầu − 24h</c>. Cả HAI vế đều cần, vì chúng chặn hai chuyện khác nhau:
    ///
    /// Vế "lúc gửi + 24h" — phụ huynh phải phản hồi trong một ngày. Học sinh gửi 9h thứ Hai thì
    /// biết chậm nhất 9h thứ Ba là có câu trả lời, không phải chờ mòn mỏi tới sát buổi học.
    ///
    /// Vế "giờ buổi đầu − 24h" — cùng luật với phụ huynh tự đặt lịch: không buổi nào được kích
    /// hoạt khi chỉ còn dưới 24 giờ. Vế này chặn ca gửi sát giờ, nơi vế trên quá rộng.
    ///
    /// Với buổi học đủ xa, vế đầu thắng và phụ huynh có đúng 24 giờ. Với buổi gần, vế sau thắng
    /// và cửa sổ co lại — tối thiểu <see cref="MinimumParentWindowHours"/> nhờ ngưỡng
    /// <see cref="MinimumLeadHoursForStudentRequest"/> chặn từ lúc chọn lịch.
    /// </summary>
    public static DateTime ResolveParentPaymentDeadline(DateTime nowUtc, DateTime firstSessionStartUtc)
    {
        var byReviewWindow = nowUtc.AddHours(ParentReviewHours);
        var byLessonLead = firstSessionStartUtc.AddHours(-MinimumLeadHours);
        return byLessonLead < byReviewWindow ? byLessonLead : byReviewWindow;
    }

    /// <summary>Buổi học có đủ xa để học sinh gửi yêu cầu cho phụ huynh không.</summary>
    public static bool IsFarEnoughForStudentRequest(DateTime nowUtc, DateTime sessionStartUtc)
        => sessionStartUtc >= nowUtc.AddHours(MinimumLeadHoursForStudentRequest);
}
