using System;
using MV.DomainLayer.Constants;

namespace MV.DomainLayer.Entities;

/// <summary>
/// Bộ câu hỏi gia sư tạo nhanh trong buổi học (`practice_sets`).
///
/// Gắn BOOKING chứ không phải buổi học: học sinh mở lại được bài tập của cả khoá,
/// gia sư tái dùng bộ đề cho buổi sau. <see cref="ClassSessionId"/> chỉ ghi nhận
/// bộ này sinh ra ở buổi nào.
///
/// TÁCH khỏi <see cref="QuestionBank"/> có chủ đích — xem comment đầu file
/// migrations/V20260829__practice_sets_and_material_contents.sql.
/// </summary>
public partial class SessionPracticeSet
{
    public Guid Id { get; set; }

    public int BookingId { get; set; }

    /// <summary>Buổi học lúc tạo. Null nếu gia sư soạn ngoài buổi.</summary>
    public int? ClassSessionId { get; set; }

    public string TutorId { get; set; } = null!;

    public string Title { get; set; } = null!;

    /// <summary>Prompt gia sư đã gõ — giữ lại để tạo lại bộ tương tự / soi khi đề lệch.</summary>
    public string? Prompt { get; set; }

    /// <summary>draft = chỉ gia sư thấy | sent = học sinh thấy.</summary>
    public string Status { get; set; } = SessionPracticeSetStatus.Draft;

    public DateTime? SentAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Booking? Booking { get; set; }

    public virtual ICollection<SessionPracticeQuestion> Questions { get; set; } = new List<SessionPracticeQuestion>();

    /// <summary>Tài liệu nguồn AI đã đọc để sinh bộ này (N-N).</summary>
    public virtual ICollection<Learningmaterial> Materials { get; set; } = new List<Learningmaterial>();
}
