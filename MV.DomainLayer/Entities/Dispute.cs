using System;
using System.Collections.Generic;

namespace MV.DomainLayer.Entities;

public partial class Dispute
{
    public int Disputeid { get; set; }

    public int? Bookingid { get; set; }

    public int? Classsessionid { get; set; }

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

    public string? Tutorresponse { get; set; }

    public DateTime? Tutorrespondedat { get; set; }

    public DateTime? Noshowconfirmedat { get; set; }

    public string? Noshowconfirmedby { get; set; }

    /// <summary>AI-classified priority (low/medium/high) — see <see cref="Constants.DisputePriority"/>.</summary>
    public string? Priority { get; set; }

    /// <summary>Short justification from the AI classifier for <see cref="Priority"/>.</summary>
    public string? Priorityreason { get; set; }

    public virtual Booking? Booking { get; set; }

    public virtual User? CreatedbyNavigation { get; set; }

    public virtual ClassSession? ClassSession { get; set; }

    public virtual User? ResolvedbyNavigation { get; set; }

    public virtual ICollection<DisputeEvidence> DisputeEvidences { get; set; } = new List<DisputeEvidence>();

    public virtual ICollection<DisputeMessage> DisputeMessages { get; set; } = new List<DisputeMessage>();
}
