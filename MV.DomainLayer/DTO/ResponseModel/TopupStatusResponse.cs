namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// Trạng thái một lệnh nạp ví (dùng cho FE poll trong luồng "nạp bù rồi thanh toán booking").
/// <see cref="WalletCredited"/> là cờ FE dựa vào để tự động gọi thanh toán bằng ví.
/// </summary>
public class TopupStatusResponse
{
    public long OrderCode { get; set; }

    /// <summary>Trạng thái thô: TopupStatus (pending/completed) hoặc PayOSLinkStatus (PAID/EXPIRED/CANCELLED...).</summary>
    public string Status { get; set; } = "";

    /// <summary>true khi số dư ví đã thực sự được cộng cho lệnh nạp này.</summary>
    public bool WalletCredited { get; set; }

    public decimal Amount { get; set; }

    /// <summary>Số dư ví hiện tại (sau khi đã cộng nếu có), để FE xác nhận đủ tiền trả booking.</summary>
    public decimal WalletBalance { get; set; }
}
