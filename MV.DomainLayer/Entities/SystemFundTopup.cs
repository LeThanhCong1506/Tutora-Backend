using System;

namespace MV.DomainLayer.Entities;

/// <summary>
/// Một lần admin nạp tiền thật vào quỹ hệ thống — kèm ảnh chứng minh, giống payout thủ công.
/// </summary>
public partial class SystemFundTopup
{
    public long Topupid { get; set; }

    public decimal Amount { get; set; }

    public string Reason { get; set; } = null!;

    public string Proofimagepath { get; set; } = null!;

    public string Createdby { get; set; } = null!;

    public DateTime? Createdat { get; set; }

    public virtual User? CreatedbyNavigation { get; set; }
}
