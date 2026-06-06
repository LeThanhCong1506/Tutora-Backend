using System;
using System.Collections.Generic;

namespace MV.DomainLayer.Entities;

public partial class Dispute
{
    public int Disputeid { get; set; }

    public int? Bookingid { get; set; }

    public int? Lessonid { get; set; }

    public string? Createdby { get; set; }

    public string? Reason { get; set; }

    public string? Status { get; set; }

    public string? Resolvedby { get; set; }

    public string? Resolutionnote { get; set; }

    public bool? Refundissued { get; set; }

    public DateTime? Createdat { get; set; }

    public string? Disputetype { get; set; }

    public string? Evidence { get; set; }

    public decimal? Refundamount { get; set; }

    public int? Refundpercentage { get; set; }

    public DateTime? Resolvedat { get; set; }

    public virtual Booking? Booking { get; set; }

    public virtual User? CreatedbyNavigation { get; set; }

    public virtual Lesson? Lesson { get; set; }

    public virtual User? ResolvedbyNavigation { get; set; }
}
