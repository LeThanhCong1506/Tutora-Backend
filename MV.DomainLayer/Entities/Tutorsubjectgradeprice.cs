using System;
using System.Collections.Generic;

namespace MV.DomainLayer.Entities;

public partial class Tutorsubjectgradeprice
{
    public int Id { get; set; }

    public string Tutorid { get; set; } = null!;

    public int Subjectid { get; set; }

    public int Gradelevelid { get; set; }

    public decimal Priceperhour { get; set; }

    public string Currency { get; set; } = "VND";

    public bool Isactive { get; set; } = true;

    public DateTime? Createdat { get; set; }

    public DateTime? Updatedat { get; set; }

    public virtual Gradelevel Gradelevel { get; set; } = null!;

    public virtual Subject Subject { get; set; } = null!;

    public virtual Tutorprofile Tutor { get; set; } = null!;

    public virtual ICollection<Booking> Bookings { get; set; } = new List<Booking>();
}
