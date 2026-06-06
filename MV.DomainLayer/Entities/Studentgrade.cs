using System;
using System.Collections.Generic;

namespace MV.DomainLayer.Entities;

public partial class Studentgrade
{
    public int Gradeid { get; set; }

    public string? Studentid { get; set; }

    public int? Bookingid { get; set; }

    public string? Tutorid { get; set; }

    public int? Subjectid { get; set; }

    public string? Examname { get; set; }

    public string? Examtype { get; set; }

    public decimal? Score { get; set; }

    public decimal? Maxscore { get; set; }

    public DateOnly? Examdate { get; set; }

    public string? Notes { get; set; }

    public DateTime? Createdat { get; set; }

    public virtual Booking? Booking { get; set; }

    public virtual Studentprofile? Student { get; set; }

    public virtual Subject? Subject { get; set; }

    public virtual Tutorprofile? Tutor { get; set; }
}
