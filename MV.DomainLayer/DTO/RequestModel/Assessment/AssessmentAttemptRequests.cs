using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel.Assessment;

/// <summary>1 câu trả lời khi nộp bài.</summary>
public class SubmitAnswerRequest
{
    [Required(ErrorMessage = "Thiếu id câu hỏi")]
    public Guid QuestionId { get; set; }

    /// <summary>CSV key, hoặc chuỗi với loại nhập tay. Null = bỏ trống.</summary>
    public string? GivenAnswer { get; set; }

    /// <summary>Giây.</summary>
    [Range(0, 86400, ErrorMessage = "Thời gian làm câu không hợp lệ")]
    public int? TimeSpentSeconds { get; set; }
}

/// <summary>Nộp bài. Câu vắng mặt tính là bỏ trống.</summary>
public class SubmitAttemptRequest
{
    public List<SubmitAnswerRequest> Answers { get; set; } = new();
}

/// <summary>Kết quả AI phân tích ghi ngược về BE. JSON nhận nguyên khối, không parse.</summary>
public class SaveAnalysisRequest
{
    /// <summary>Markdown, cho học sinh đọc.</summary>
    public string? Summary { get; set; }

    /// <summary>beginner | developing | proficient | advanced.</summary>
    public string? Level { get; set; }

    /// <summary>JSON điểm mạnh theo chương.</summary>
    public string? Strengths { get; set; }

    /// <summary>JSON lỗ hổng theo chương.</summary>
    public string? Weaknesses { get; set; }

    /// <summary>JSON lộ trình đề xuất.</summary>
    public string? RecommendedPath { get; set; }

    /// <summary>JSON đầy đủ, lưu nguyên để truy vết.</summary>
    public string? AnalysisResult { get; set; }
}
