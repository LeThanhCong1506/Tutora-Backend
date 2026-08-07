using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel;

/// <summary>
/// Sửa đánh giá khóa học đã gửi. Booking không đổi được nên không có BookingId ở đây.
/// </summary>
public class UpdateFeedbackRequest
{
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
