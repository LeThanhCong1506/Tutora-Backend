using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel.Assessment;

/// <summary>Tạo bộ đề. Đề mới luôn rỗng câu.</summary>
public class CreateAssessmentRequest
{
    [Required(ErrorMessage = "Tên đề là bắt buộc")]
    [StringLength(255, MinimumLength = 3, ErrorMessage = "Tên đề phải từ 3 đến 255 ký tự")]
    public string Title { get; set; } = null!;

    public string? Description { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn môn học")]
    public int SubjectId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn khối lớp")]
    public int GradeLevelId { get; set; }

    /// <summary>NULL = làm hết câu đã gán.</summary>
    [Range(1, 500, ErrorMessage = "Số câu hỏi phải từ 1 đến 500")]
    public int? QuestionCount { get; set; }

    /// <summary>NULL = không giới hạn.</summary>
    [Range(1, 600, ErrorMessage = "Thời gian làm bài phải từ 1 đến 600 phút")]
    public int? DurationMinutes { get; set; }

    public bool ShuffleQuestions { get; set; }

    public bool ShuffleOptions { get; set; }

    public bool ShowResult { get; set; } = true;

    /// <summary>draft | published | archived. Mặc định draft.</summary>
    public string? Status { get; set; }
}

/// <summary>Cập nhật cấu hình đề, không đụng danh sách câu.</summary>
public class UpdateAssessmentRequest
{
    [Required(ErrorMessage = "Tên đề là bắt buộc")]
    [StringLength(255, MinimumLength = 3, ErrorMessage = "Tên đề phải từ 3 đến 255 ký tự")]
    public string Title { get; set; } = null!;

    public string? Description { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn môn học")]
    public int SubjectId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn khối lớp")]
    public int GradeLevelId { get; set; }

    [Range(1, 500, ErrorMessage = "Số câu hỏi phải từ 1 đến 500")]
    public int? QuestionCount { get; set; }

    [Range(1, 600, ErrorMessage = "Thời gian làm bài phải từ 1 đến 600 phút")]
    public int? DurationMinutes { get; set; }

    public bool ShuffleQuestions { get; set; }

    public bool ShuffleOptions { get; set; }

    public bool ShowResult { get; set; } = true;

    /// <summary>Bỏ trống = giữ nguyên.</summary>
    public string? Status { get; set; }
}

/// <summary>Đổi trạng thái đề.</summary>
public class UpdateAssessmentStatusRequest
{
    [Required(ErrorMessage = "Trạng thái là bắt buộc")]
    public string Status { get; set; } = null!;
}
