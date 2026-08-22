using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using MV.DomainLayer.Attributes;

namespace MV.DomainLayer.DTO.RequestModel.Admin;

public class ApproveWithdrawalRequest
{
    [Required(ErrorMessage = "Vui lòng nhập thời gian giao dịch.")]
    public DateTimeOffset? PaidAt { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập ghi chú đối soát giao dịch chuyển khoản.")]
    [MinLength(3, ErrorMessage = "Ghi chú quá ngắn.")]
    [MaxLength(500, ErrorMessage = "Ghi chú không được vượt quá 500 ký tự.")]
    public string Note { get; set; } = string.Empty;

    /// <summary>
    /// Mã THAM CHIẾU (reference number, thường có tiền tố "FT") trên biên lai chuyển khoản —
    /// đây là mã liên ngân hàng (qua Napas) dùng để tra cứu/khiếu nại với ngân hàng, KHÁC với
    /// "mã giao dịch" nội bộ mà app ngân hàng hiện thêm trên cùng biên lai (mã đó chỉ có ý
    /// nghĩa trong phạm vi app của ngân hàng đó, không dùng để đối soát chéo được).
    /// Bắt buộc vì đây là thứ duy nhất khớp được lệnh chi trong hệ thống với đúng dòng trên
    /// sao kê — ảnh biên lai chỉ đối soát được bằng mắt. KHÔNG phải mã đối soát nội bộ của
    /// Tutora, vốn do backend tự sinh và không bao giờ nhận từ client.
    /// </summary>
    [Required(ErrorMessage = "Vui lòng nhập mã tham chiếu của ngân hàng.")]
    [MinLength(4, ErrorMessage = "Mã tham chiếu ngân hàng quá ngắn.")]
    [MaxLength(100, ErrorMessage = "Mã tham chiếu ngân hàng không được vượt quá 100 ký tự.")]
    [RegularExpression("^[A-Za-z0-9._/-]+$",
        ErrorMessage = "Mã tham chiếu ngân hàng chỉ được gồm chữ, số và các ký tự . _ - /")]
    public string BankTransactionCode { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng tải ảnh biên lai chuyển khoản.")]
    [ImageFile]
    public IFormFile ProofImage { get; set; } = null!;
}
