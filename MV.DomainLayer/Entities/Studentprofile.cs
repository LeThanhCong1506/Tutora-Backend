using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace MV.DomainLayer.Entities;

public partial class Studentprofile
{
    public string Studentid { get; set; } = null!;

    public string? Parentid { get; set; }

    public string? Linkeduserid { get; set; }

    public string? Fullname { get; set; }

    public DateOnly? Birthdate { get; set; }

    public string? School { get; set; }

    public int? Gradelevelid { get; set; }

    [NotMapped]
    public string? Gradelevel
    {
        get => GradelevelNavigation?.Gradename;
        set { }
    }

    public string? Learninggoals { get; set; }

    public string? Avatarurl { get; set; }

    public DateTime? Createdat { get; set; }

    public string? Studentcode { get; set; }

    public DateTime? Studentcodeexpiresat { get; set; }

    public DateTime? Deletedat { get; set; }

    public virtual ICollection<Booking> Bookings { get; set; } = new List<Booking>();

    public virtual ICollection<Handoversummary> Handoversummaries { get; set; } = new List<Handoversummary>();

    public virtual ICollection<Learningmaterial> Learningmaterials { get; set; } = new List<Learningmaterial>();

    public virtual ICollection<ClassSession> ClassSessions { get; set; } = new List<ClassSession>();

    public virtual User? Linkeduser { get; set; }

    public virtual User? Parent { get; set; }

    public virtual Gradelevel? GradelevelNavigation { get; set; }

    public virtual ICollection<Studentgrade> Studentgrades { get; set; } = new List<Studentgrade>();
}
