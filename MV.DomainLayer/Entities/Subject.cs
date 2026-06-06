using System;
using System.Collections.Generic;

namespace MV.DomainLayer.Entities;

public partial class Subject
{
    public int Subjectid { get; set; }

    public string? Subjectname { get; set; }

    public string? Description { get; set; }

    public virtual ICollection<Studentgrade> Studentgrades { get; set; } = new List<Studentgrade>();

    public virtual ICollection<Tutorsubjectgradeprice> Tutorsubjectgradeprices { get; set; } = new List<Tutorsubjectgradeprice>();
}
