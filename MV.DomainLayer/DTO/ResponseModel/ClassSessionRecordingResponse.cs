namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// Trạng thái + link xem lại bản ghi video buổi học, cho Tutor/Student/Parent xem qua app.
/// StreamUrl là link tạm (ký token ngắn hạn) trỏ tới endpoint proxy — không phải link Drive trực tiếp.
/// </summary>
public class ClassSessionRecordingResponse
{
    public int ClassSessionId { get; set; }

    /// <summary>available (có thể xem) | processing (đang đẩy lên Drive) | recording (đang ghi) | none.</summary>
    public string Status { get; set; } = "none";

    /// <summary>Link proxy để phát video — chỉ có khi Status = "available". Hết hạn sau ít phút.</summary>
    public string? StreamUrl { get; set; }

    /// <summary>True nếu đã có thể xem được.</summary>
    public bool Available { get; set; }
}
