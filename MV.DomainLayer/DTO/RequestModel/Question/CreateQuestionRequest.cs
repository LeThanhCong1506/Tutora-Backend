using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel.Question;

/// <summary>
/// Staff/admin tạo 1 câu hỏi vào question bank. Sau khi tạo, hệ thống gọi
/// tutora-ai để embed (content -> vector) rồi lưu embedding.
/// </summary>
public class CreateQuestionRequest
{
    [Required(ErrorMessage = "Môn học là bắt buộc")]
    public int SubjectId { get; set; }

    [Required(ErrorMessage = "Khối lớp là bắt buộc")]
    public int GradeLevelId { get; set; }

    /// <summary>Chương/chủ đề (chương trình VN), vd "ung_dung_dao_ham".</summary>
    public string? Chapter { get; set; }

    /// <summary>tu_luan | trac_nghiem | dien_so.</summary>
    public string? ProblemType { get; set; }

    // Sau nếu được thì cải thiện lên 4 cấp: nhận biết - thông hiểu - vận dụng - vận dụng cao
    [Range(1, 5, ErrorMessage = "Độ khó phải từ 1 đến 5")]
    public short? Difficulty { get; set; }

    [Required(ErrorMessage = "Nội dung câu hỏi là bắt buộc")]
    [MinLength(5, ErrorMessage = "Nội dung câu hỏi quá ngắn")]
    public string Content { get; set; } = null!;

    /// <summary>Lời giải mẫu (thầy cô/Bộ GD). Optional.</summary>
    public string? Solution { get; set; }

    public string? SolutionSource { get; set; }

    /// <summary>pending_review | published. Mặc định pending_review (chờ duyệt).</summary>
    public string? ReviewStatus { get; set; }
}
