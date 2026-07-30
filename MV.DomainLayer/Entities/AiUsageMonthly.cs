using System;

namespace MV.DomainLayer.Entities;

/// <summary>
/// Lượt dùng AI gộp theo (tài khoản, tháng).
/// </summary>
public partial class AiUsageMonthly
{
    public string Userid { get; set; } = null!;

    public DateOnly Period { get; set; }

    public int Usedcount { get; set; }

    public DateTime Updatedat { get; set; }

    public virtual User? User { get; set; }
}
