using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel;

/// <summary>
/// Request for tutor to submit lesson report after class
/// </summary>
public class SubmitReportRequest
{
    /// <summary>
    /// Content covered in the lesson
    /// </summary>
    [Required(ErrorMessage = "Nội dung buổi học là bắt buộc")]
    [StringLength(2000, ErrorMessage = "Nội dung không được vượt quá 2000 ký tự")]
    public string ContentCovered { get; set; } = null!;

    /// <summary>
    /// Homework assigned to student
    /// </summary>
    [StringLength(1000, ErrorMessage = "Bài tập về nhà không được vượt quá 1000 ký tự")]
    public string? HomeworkAssigned { get; set; }

    /// <summary>
    /// Student performance rating (1-5)
    /// </summary>
    [Range(1, 5, ErrorMessage = "Đánh giá phải từ 1 đến 5")]
    public int? StudentPerformanceRating { get; set; }

    /// <summary>
    /// Additional tutor notes
    /// </summary>
    [StringLength(1000, ErrorMessage = "Ghi chú không được vượt quá 1000 ký tự")]
    public string? TutorNotes { get; set; }

    /// <summary>
    /// Whether tutor was present
    /// </summary>
    public bool IsTutorPresent { get; set; } = true;

    /// <summary>
    /// Whether student was present
    /// </summary>
    public bool IsStudentPresent { get; set; } = true;

    /// <summary>
    /// Attendance note (if student was absent)
    /// </summary>
    [StringLength(500, ErrorMessage = "Ghi chú điểm danh không được vượt quá 500 ký tự")]
    public string? AttendanceNote { get; set; }

    /// <summary>
    /// List of attachment URLs (slides, exercises, etc.)
    /// </summary>
    public List<string>? Attachments { get; set; }
}
