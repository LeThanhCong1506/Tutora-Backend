namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// Trạng thái đồng ý bỏ buổi phụ (link 2, sinh ra khi buổi gốc bị báo ngắt) — cần CẢ HAI phía
/// (gia sư + học sinh/phụ huynh) cùng xác nhận thì buổi phụ mới bị huỷ và buổi gốc mới nhận được
/// báo cáo, xem ClassSessionService.ConfirmSkipContinuationAsync/SubmitReportAsync.
/// </summary>
public class ClassSessionSkipContinuationResponse
{
    public bool TutorConfirmed { get; set; }
    public bool StudentConfirmed { get; set; }
    public bool BothConfirmed { get; set; }
}
