using System;

namespace MV.DomainLayer.Entities;

/// <summary>
/// Tín hiệu chủ đề: mỗi lần học sinh nhờ AI giải một bài, classifier (tutora-ai) đoán ra
/// lớp/chương/dạng toán — lưu lại ở đây để biết em đang vướng chương nào.
/// </summary>
public partial class StudentTopicSignal
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = null!;

    public Guid SessionId { get; set; }

    /// <summary>Message của AI sinh ra tín hiệu này (để truy vết về đúng bài đã hỏi).</summary>
    public Guid? MessageId { get; set; }

    /// <summary>
    /// Slug lớp theo classifier: "9" | "10" | "11" | "12" | "thi_vao_10" | "thi_thpt".
    /// KHÔNG phải Gradelevelid (lớp 9 = id 57).
    /// </summary>
    public string? Grade { get; set; }

    public string ChapterSlug { get; set; } = null!;

    public string? Topic { get; set; }

    /// <summary>
    /// Độ tự tin do LLM tự chấm — kém hiệu chuẩn, chỉ dùng để lọc thô chứ không làm
    /// trọng số xếp hạng.
    /// </summary>
    public float Confidence { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual User? User { get; set; }

    public virtual ChatSession? Session { get; set; }
}
