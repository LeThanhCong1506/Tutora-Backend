using System;
using System.Collections.Generic;

namespace MV.DomainLayer.Entities;

public partial class Userwarning
{
    public int Warningid { get; set; }

    public string? Userid { get; set; }

    public int Warninglevel { get; set; }

    public string? Reason { get; set; }

    public int? Relatedbookingid { get; set; }

    public string? Issuedby { get; set; }

    public DateTime? Createdat { get; set; }

    public virtual User? IssuedbyNavigation { get; set; }

    public virtual Booking? Relatedbooking { get; set; }

    public virtual User? User { get; set; }
}
