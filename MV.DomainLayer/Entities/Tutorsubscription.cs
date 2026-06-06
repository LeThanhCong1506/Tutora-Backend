using System;
using System.Collections.Generic;

namespace MV.DomainLayer.Entities;

public partial class Tutorsubscription
{
    public int Subscriptionid { get; set; }

    public string? Tutorid { get; set; }

    public string Packagetype { get; set; } = null!;

    public DateTime? Startdate { get; set; }

    public DateTime? Enddate { get; set; }

    public decimal? Price { get; set; }

    public string? Paymentstatus { get; set; }

    public bool? Isactive { get; set; }

    public virtual Tutorprofile? Tutor { get; set; }
}
