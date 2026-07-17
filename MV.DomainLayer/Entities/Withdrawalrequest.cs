using System;
using System.Collections.Generic;

namespace MV.DomainLayer.Entities;

public partial class Withdrawalrequest
{
    public int Withdrawalid { get; set; }

    public string? Userid { get; set; }

    /// <summary>Ví thực tế bị trừ tiền khi rút. FK tới wallets(wallet_id).</summary>
    public int? Walletid { get; set; }

    public decimal? Amount { get; set; }

    public string? Bankname { get; set; }

    public string? Accountnumber { get; set; }

    public string? Accountholdername { get; set; }

    public string? Status { get; set; }

    public DateTime? Requestedat { get; set; }

    public DateTime? Processedat { get; set; }

    public string? Decision { get; set; }

    public string? Processedby { get; set; }

    /// <summary>Ghi chú bắt buộc do staff nhập khi đánh dấu đã chuyển tiền thủ công (mã giao dịch, thời gian...).</summary>
    public string? Completionnote { get; set; }

    public string? Claimedby { get; set; }

    public DateTime? Claimedat { get; set; }

    public string? Rejectionreason { get; set; }

    public virtual User? User { get; set; }

    public virtual Wallet? Wallet { get; set; }
}
