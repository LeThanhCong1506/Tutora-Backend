using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel;

/// <summary>
/// Request for parent/student to review a completed booking.
/// Mỗi người chỉ đánh giá được một lần cho mỗi booking (ràng buộc UNIQUE(booking_id, from_user_id)).
/// </summary>
public class CreateFeedbackRequest
{
    /// <summary>
    /// Booking ID being reviewed
    /// </summary>
    [Required(ErrorMessage = "Vui lòng cung cấp BookingId để đánh giá")]
    public int BookingId { get; set; }

    /// <summary>
    /// Rating from 1 to 5
    /// </summary>
    [Required(ErrorMessage = "Đánh giá sao là bắt buộc")]
    [Range(1, 5, ErrorMessage = "Đánh giá phải từ 1 đến 5 sao")]
    public int Rating { get; set; }

    /// <summary>
    /// Comment about the course/tutor
    /// </summary>
    [StringLength(1000, ErrorMessage = "Nhận xét không được vượt quá 1000 ký tự")]
    public string? Comment { get; set; }

    /// <summary>
    /// Initial learning goal
    /// </summary>
    [StringLength(500)]
    public string? InitialGoal { get; set; }

    /// <summary>
    /// Actual result achieved
    /// </summary>
    [StringLength(500)]
    public string? ActualResult { get; set; }

    /// <summary>
    /// Course duration feedback
    /// </summary>
    [StringLength(200)]
    public string? CourseDuration { get; set; }
}
