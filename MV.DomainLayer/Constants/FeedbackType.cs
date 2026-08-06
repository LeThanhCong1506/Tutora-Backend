namespace MV.DomainLayer.Constants;

/// <summary>
/// Feedback type constants.
/// </summary>
public static class FeedbackType
{
    /// <summary>
    /// Đánh giá khóa học — loại duy nhất được tạo mới. Người học đánh giá một lần cho cả
    /// booking sau khi booking đã hoàn thành; điểm này tính vào rating của gia sư trên marketplace.
    /// </summary>
    public const string BookingReview = "booking_review";

    // --- Legacy: chỉ dùng để đọc dữ liệu cũ, không tạo mới ---

    /// <summary>Đánh giá theo từng buổi học (đã bỏ).</summary>
    public const string PostLesson      = "post_lesson";

    /// <summary>Đánh giá của phụ huynh cho gia sư (đã bỏ).</summary>
    public const string ParentToTutor   = "parent_to_tutor";

    /// <summary>Đánh giá khi kết thúc khóa sớm (đã gộp vào <see cref="BookingReview"/>).</summary>
    public const string EarlyTermination = "early_termination";
}
