using System;
using System.Collections.Generic;

namespace MV.DomainLayer.Entities;

public partial class Tutorpackage
{
    public const int FlexiblePackageType = 1;
    public const int FixedPackageType = 2;

    public int Packageid { get; set; }

    public string Tutorid { get; set; } = null!;

    public string Name { get; set; } = null!;

    public int Packagetype { get; set; }

    public bool Isactive { get; set; } = true;

    public DateTime? Createdat { get; set; }

    public DateTime? Updatedat { get; set; }

    public virtual Tutorprofile Tutor { get; set; } = null!;

    public virtual ICollection<Tutorpackagefixedslot> Tutorpackagefixedslots { get; set; } = new List<Tutorpackagefixedslot>();

    public virtual ICollection<Booking> Bookings { get; set; } = new List<Booking>();
}
