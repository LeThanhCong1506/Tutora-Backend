namespace MV.DomainLayer.DTO.RequestModel;

/// <summary>
/// Gia sư/học sinh/phụ huynh báo buổi học đang <c>in_progress</c> bị ngắt giữa chừng vì sự cố
/// đột xuất (mất điện, mất mạng...). Lý do không bắt buộc ở tầng service — <c>RequestInterruptionAsync</c>
/// nhận <c>reason</c> là <c>string?</c> — nhưng FE nên yêu cầu người dùng nhập để hỗ trợ tra cứu sau này.
/// </summary>
public class RequestInterruptionRequest
{
    public string? Reason { get; set; }
}
