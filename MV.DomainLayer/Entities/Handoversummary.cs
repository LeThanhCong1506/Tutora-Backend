using System;
using System.Collections.Generic;

namespace MV.DomainLayer.Entities;

public partial class Handoversummary
{
    public int Summaryid { get; set; }

    public string? Studentid { get; set; }

    public int? Frombookingid { get; set; }

    public string? Fromtutorid { get; set; }

    public string? Totutorid { get; set; }

    public int? Totalsessions { get; set; }

    public decimal? Attendancerate { get; set; }

    public decimal? Averagescore { get; set; }

    public string? Scoretrend { get; set; }

    public string? Topicscovered { get; set; }

    public string? Tutornotes { get; set; }

    public DateTime? Generatedat { get; set; }

    public virtual Booking? Frombooking { get; set; }

    public virtual Tutorprofile? Fromtutor { get; set; }

    public virtual Studentprofile? Student { get; set; }

    public virtual Tutorprofile? Totutor { get; set; }
}
