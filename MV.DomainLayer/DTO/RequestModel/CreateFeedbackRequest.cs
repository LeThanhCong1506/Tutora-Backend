using System.ComponentModel.DataAnnotations;
using MV.DomainLayer.Constants;

namespace MV.DomainLayer.DTO.RequestModel;

/// <summary>
/// Request for parent/student to create feedback for tutor
/// </summary>
public class CreateFeedbackRequest
{
    /// <summary>
    /// ClassSession ID being reviewed
    /// </summary>
    public int? ClassSessionId { get; set; }

    /// <summary>
    /// Booking ID being reviewed (used for early termination feedback)
    /// </summary>
    public int? BookingId { get; set; }

    /// <summary>
    /// Rating from 1 to 5
    /// </summary>
    [Required(ErrorMessage = "Đánh giá sao là bắt buộc")]
    [Range(1, 5, ErrorMessage = "Đánh giá phải từ 1 đến 5 sao")]
    public int Rating { get; set; }

    /// <summary>
    /// Comment about the classSession/tutor
    /// </summary>
    [StringLength(1000, ErrorMessage = "Nhận xét không được vượt quá 1000 ký tự")]
    public string? Comment { get; set; }

    /// <summary>
    /// Feedback type: post_lesson, early_cancellation
    /// </summary>
    public string FeedbackType { get; set; } = MV.DomainLayer.Constants.FeedbackType.PostLesson;

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
