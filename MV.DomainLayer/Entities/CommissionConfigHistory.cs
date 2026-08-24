using System;

namespace MV.DomainLayer.Entities;

public partial class CommissionConfigHistory
{
    public long Historyid { get; set; }

    public decimal Parentfeepercent { get; set; }

    public decimal Tutorfeepercent { get; set; }

    public string? Changedby { get; set; }

    public DateTime Changedat { get; set; }

    public virtual User? ChangedbyNavigation { get; set; }
}
