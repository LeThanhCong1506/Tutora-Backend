using System;
using System.Collections.Generic;

namespace MV.DomainLayer.Entities;

public partial class Tutorsubject
{
    public int Id { get; set; }

    public string? Tutorid { get; set; }

    public int? Subjectid { get; set; }

    public string? Gradelevels { get; set; }

    public string? Tags { get; set; }

    public virtual Subject? Subject { get; set; }

    public virtual Tutorprofile? Tutor { get; set; }
}
