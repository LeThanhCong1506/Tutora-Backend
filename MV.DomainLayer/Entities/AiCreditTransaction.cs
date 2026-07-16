using System;

namespace MV.DomainLayer.Entities;

/// <summary>Ledger audit cho việc trừ/cấp AI credit. <see cref="User.AiCreditsBalance"/> là cache cho truy vấn nhanh.</summary>
public partial class AiCreditTransaction
{
    public int Transactionid { get; set; }

    public string Userid { get; set; } = null!;

    /// <summary>Âm = tiêu (vd gọi AI solve), dương = cấp/nạp/reset định kỳ.</summary>
    public int Amount { get; set; }

    public int Balanceafter { get; set; }

    /// <summary>'solve' | 'grant' | 'purchase' | 'daily_reset'</summary>
    public string Source { get; set; } = null!;

    public string? Referenceid { get; set; }

    public string? Description { get; set; }

    public DateTime? Createdat { get; set; }

    public virtual User? User { get; set; }
}
