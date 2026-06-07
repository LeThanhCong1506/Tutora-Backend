using System;
using System.Collections.Generic;

namespace MV.DomainLayer.Entities;

public partial class Tutoravailability
{
    public int Availabilityid { get; set; }

    public string? Tutorid { get; set; }

    public int? Dayofweek { get; set; }

    public TimeOnly? Starttime { get; set; }

    public TimeOnly? Endtime { get; set; }

    public DateTime? Createdat { get; set; }

    public virtual Tutorprofile? Tutor { get; set; }
}
