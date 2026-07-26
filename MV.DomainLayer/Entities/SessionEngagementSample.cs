using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MV.DomainLayer.Entities;

/// <summary>
/// Mẫu điểm cảm xúc / độ tập trung của học viên trong một buổi học, ghi định kỳ (append-only,
/// giống <see cref="LoginHistory"/>). Dữ liệu do MÁY HỌC VIÊN tự phân tích cục bộ (MediaPipe +
/// FER+ chạy trong trình duyệt) — ảnh khuôn mặt KHÔNG BAO GIỜ rời máy học viên. Chỉ điểm số/nhãn
/// đã tổng hợp mới gửi lên đây để làm báo cáo sau buổi. Không lưu ảnh, không lưu embedding.
/// </summary>
[Table("session_engagement_samples")]
public class SessionEngagementSample
{
    [Key]
    [Column("sample_id")]
    public long SampleId { get; set; }

    [Required]
    [Column("class_session_id")]
    public int ClassSessionId { get; set; }

    /// <summary>UserId (từ JWT) của học viên gửi mẫu. Dùng để đối chiếu quyền + báo cáo.</summary>
    [Required]
    [MaxLength(50)]
    [Column("student_user_id")]
    public string StudentUserId { get; set; } = null!;

    /// <summary>Nhãn cảm xúc trội tại thời điểm lấy mẫu (happy/neutral/sad/... hoặc "drowsy").</summary>
    [MaxLength(20)]
    [Column("emotion")]
    public string? Emotion { get; set; }

    /// <summary>Điểm tập trung [0..1] đã tổng hợp (trung bình cửa sổ gửi lên).</summary>
    [Column("engagement_score")]
    public double EngagementScore { get; set; }

    /// <summary>Có dấu hiệu buồn ngủ (nhắm mắt kéo dài) tại lát cắt này không.</summary>
    [Column("drowsy")]
    public bool Drowsy { get; set; }

    /// <summary>
    /// Lý do cảnh báo nếu mẫu này gắn với một alert (away/drowsy/distracted/stressed), null nếu
    /// chỉ là mẫu điểm thường. Cho phép truy vấn số alert cho báo cáo mà không cần bảng riêng.
    /// </summary>
    [MaxLength(20)]
    [Column("alert_reason")]
    public string? AlertReason { get; set; }

    [Column("sampled_at")]
    public DateTime SampledAt { get; set; } = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

    public virtual ClassSession? ClassSession { get; set; }
}
