using System;
using System.Collections.Generic;

namespace MV.DomainLayer.Entities;

public partial class Class
{
    public int Classid { get; set; }

    public int? Bookingid { get; set; }

    public string? Tutorid { get; set; }

    public string Classcode { get; set; } = null!;

    public string? Classname { get; set; }

    public string? Status { get; set; }

    public DateTime? Createdat { get; set; }

    public virtual Booking? Booking { get; set; }

    public virtual Tutorprofile? Tutor { get; set; }
}
