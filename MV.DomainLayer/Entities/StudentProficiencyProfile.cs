using System;

namespace MV.DomainLayer.Entities;

/// <summary>
/// Trình độ học sinh theo môn, AI kết luận. ĐỌC mỗi lần học sinh hỏi bài hoặc xin lộ trình.
/// 1 row / (user, môn), ghi đè sau mỗi lần đánh giá.
/// </summary>
public partial class StudentProficiencyProfile
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = null!;

    public int SubjectId { get; set; }

    /// <summary>Lớp lần đánh giá gần nhất.</summary>
    public int? GradeLevelId { get; set; }

    /// <summary>beginner | developing | proficient | advanced — điều chỉnh độ sâu lời giải.</summary>
    public string? Level { get; set; }

    /// <summary>Nhồi vào prompt AI giải bài, vd "vững đại số, yếu hình không gian".</summary>
    public string? Summary { get; set; }

    /// <summary>JSON thô.</summary>
    public string? Strengths { get; set; }

    public string? Weaknesses { get; set; }

    /// <summary>Lộ trình đề xuất (thứ tự chương + lý do). JSON thô.</summary>
    public string? RecommendedPath { get; set; }

    /// <summary>Bài làm sinh ra profile — truy vết.</summary>
    public Guid? SourceAttemptId { get; set; }

    /// <summary>Số lần đánh giá môn này.</summary>
    public int AttemptCount { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual Subject? Subject { get; set; }

    public virtual Gradelevel? Gradelevel { get; set; }
}
