using System;

namespace MV.DomainLayer.Entities;

/// <summary>
/// Quỹ hệ thống — bảng đơn dòng (fund_id luôn = 1) giữ số dư tiền thật admin đã nạp và chưa
/// dùng hết. "Chuyển tiền chủ động" (<see cref="AdminWalletTransfer"/>) phải trừ vào đây
/// trước khi cộng ví người nhận, để không thể phát sinh tiền vượt quá những gì công ty thật
/// sự đã chuẩn bị sẵn.
/// </summary>
public partial class SystemFund
{
    public short Fundid { get; set; }

    public decimal Balance { get; set; }

    public DateTime Updatedat { get; set; }
}
