using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel.Assessment;

/// <summary>1 phương án trả lời (hoặc 1 mệnh đề với loại Đúng/Sai).</summary>
public class AssessmentAnswerOptionRequest
{
    [Required(ErrorMessage = "Ký hiệu phương án là bắt buộc")]
    [StringLength(10, ErrorMessage = "Ký hiệu phương án tối đa 10 ký tự")]
    public string Key { get; set; } = "";

    [Required(ErrorMessage = "Nội dung phương án là bắt buộc")]
    public string Text { get; set; } = "";
}

/// <summary>Thêm câu hỏi vào đề. Ràng buộc đáp án kiểm ở AssessmentQuestionValidator.</summary>
public class CreateAssessmentQuestionRequest
{
    [Required(ErrorMessage = "Nội dung câu hỏi là bắt buộc")]
    [MinLength(5, ErrorMessage = "Nội dung câu hỏi quá ngắn")]
    public string Content { get; set; } = null!;

    /// <summary>Bắt buộc ≥2 phương án, trừ loại trả lời ngắn.</summary>
    public List<AssessmentAnswerOptionRequest>? AnswerOptions { get; set; }

    /// <summary>Trắc nghiệm -> CSV key đúng. Trả lời ngắn -> chuỗi đáp án.</summary>
    [Required(ErrorMessage = "Đáp án đúng là bắt buộc")]
    public string CorrectAnswer { get; set; } = null!;

    /// <summary>Chỉ loại trả lời ngắn: các cách viết khác cũng tính đúng.</summary>
    public List<string>? AcceptedAnswers { get; set; }

    public string? Explanation { get; set; }

    public int? ChapterId { get; set; }

    /// <summary>FK question_types. Slug quyết định cách chấm — xem QuestionTypeFormatMapper.</summary>
    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn loại câu hỏi")]
    public int QuestionTypeId { get; set; }

    /// <summary>NHAN_BIET | THONG_HIEU | VAN_DUNG | VAN_DUNG_CAO.</summary>
    public string? Difficulty { get; set; }

    /// <summary>Mặc định 1.</summary>
    [Range(0.01, 100, ErrorMessage = "Điểm mỗi câu phải từ 0.01 đến 100")]
    public decimal Points { get; set; } = 1;

    /// <summary>Bỏ trống = thêm cuối.</summary>
    public int? DisplayOrder { get; set; }

    public List<string>? ImageUrls { get; set; }
}

/// <summary>Sửa câu — thay toàn bộ, không phải PATCH.</summary>
public class UpdateAssessmentQuestionRequest : CreateAssessmentQuestionRequest
{
}

/// <summary>Sắp lại thứ tự câu.</summary>
public class ReorderAssessmentQuestionsRequest
{
    /// <summary>Id câu theo thứ tự mong muốn. Phải đủ mọi câu của đề.</summary>
    [Required(ErrorMessage = "Danh sách câu hỏi là bắt buộc")]
    [MinLength(1, ErrorMessage = "Danh sách câu hỏi không được rỗng")]
    public List<Guid> QuestionIds { get; set; } = new();
}
