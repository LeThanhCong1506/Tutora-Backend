using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel;

/// <summary>
/// Một mẫu điểm cảm xúc / độ tập trung đã tổng hợp, do máy học viên gửi lên định kỳ.
/// KHÔNG chứa ảnh — chỉ điểm số và nhãn. Client gửi theo lô để giảm số request.
/// </summary>
public class EngagementSampleItem
{
    /// <summary>Nhãn cảm xúc trội (happy/neutral/sad/angry/fear/surprise/disgust/contempt) hoặc "drowsy".</summary>
    [StringLength(20)]
    public string? Emotion { get; set; }

    /// <summary>Điểm tập trung [0..1].</summary>
    [Range(0, 1)]
    public double EngagementScore { get; set; }

    public bool Drowsy { get; set; }
}

/// <summary>Payload gửi lô mẫu điểm (đường ingest, ~mỗi 15-30s).</summary>
public class EngagementSampleBatchRequest
{
    [Required]
    public List<EngagementSampleItem> Samples { get; set; } = new();
}

/// <summary>
/// Cảnh báo realtime do AlertEngine trên máy học viên phát ra (rời màn hình / buồn ngủ /
/// mất tập trung / căng thẳng). BE đẩy tới gia sư qua SignalR để hiện toast.
/// </summary>
public class EngagementAlertRequest
{
    /// <summary>Mã lý do: away | drowsy | distracted | stressed.</summary>
    [Required]
    [StringLength(20)]
    public string Reason { get; set; } = null!;

    /// <summary>Mức độ: HIGH | MED.</summary>
    [StringLength(10)]
    public string? Level { get; set; }

    /// <summary>Thông điệp hiển thị cho gia sư (đã tiếng Việt sẵn từ client).</summary>
    [Required]
    [StringLength(255)]
    public string Message { get; set; } = null!;

    /// <summary>Điểm tập trung tại thời điểm cảnh báo (tuỳ chọn, để lưu kèm mẫu).</summary>
    [Range(0, 1)]
    public double? EngagementScore { get; set; }
}
