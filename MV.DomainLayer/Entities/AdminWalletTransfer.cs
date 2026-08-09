using System;

namespace MV.DomainLayer.Entities;

/// <summary>
/// Bản ghi kiểm toán cho một lần admin/staff chủ động cộng tiền vào ví một user — không gắn
/// với booking hay yêu cầu rút tiền nào. Tiền được cộng thẳng vào <see cref="Wallet.Balance"/>
/// của người nhận ngay khi tạo (không qua bước duyệt thứ hai); bảng này chỉ giữ lại ai làm,
/// cho ai, bao nhiêu và vì sao.
/// </summary>
public partial class AdminWalletTransfer
{
    public int Transferid { get; set; }

    public string Recipientuserid { get; set; } = null!;

    public decimal Amount { get; set; }

    public string Reason { get; set; } = null!;

    public string Createdby { get; set; } = null!;

    /// <summary>Trỏ tới dòng Wallettransaction (Transactiontype = AdminCredit) đã ghi khi cộng tiền.</summary>
    public int? Wallettransactionid { get; set; }

    public DateTime? Createdat { get; set; }
}
