using System;

namespace MV.DomainLayer.Entities;

public partial class Tutorpackagefixedslot
{
    public int Fixedslotid { get; set; }

    public int Packageid { get; set; }

    public int Dayofweek { get; set; }

    public TimeOnly Starttime { get; set; }

    public TimeOnly Endtime { get; set; }

    public DateTime? Createdat { get; set; }

    public virtual Tutorpackage Package { get; set; } = null!;
    public virtual Dayofweek DayofweekNavigation { get; set; } = null!;
}
