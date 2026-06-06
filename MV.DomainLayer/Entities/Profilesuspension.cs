using System;
using System.Collections.Generic;

namespace MV.DomainLayer.Entities;

public partial class Profilesuspension
{
    public int Suspensionid { get; set; }

    public string? Userid { get; set; }

    public string Suspensiontype { get; set; } = null!;

    public string? Reason { get; set; }

    public DateTime? Startdate { get; set; }

    public DateTime? Enddate { get; set; }

    public bool? Isactive { get; set; }

    public string? Createdby { get; set; }

    public virtual User? CreatedbyNavigation { get; set; }

    public virtual User? User { get; set; }
}
