using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel;

/// <summary>
/// Ẩn hoặc hiện lại một đánh giá. Lý do bắt buộc khi ẩn — nó được gửi thẳng cho người viết
/// đánh giá nên phải nói rõ vì sao; khi hiện lại thì bỏ qua.
/// </summary>
public class ToggleFeedbackVisibilityRequest
{
    [StringLength(500, ErrorMessage = "Lý do không được vượt quá 500 ký tự")]
    public string? Reason { get; set; }
}
