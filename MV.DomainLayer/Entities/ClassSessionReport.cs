using System;
using System.Collections.Generic;

namespace MV.DomainLayer.Entities;

public partial class ClassSessionReport
{
    public int Reportid { get; set; }

    public int? Classsessionid { get; set; }

    public string? Createdbytutorid { get; set; }

    public string? Contentcovered { get; set; }

    public string? Homeworkassigned { get; set; }

    public int? Studentperformancerating { get; set; }

    public string? Attachments { get; set; }

    public DateTime? Createdat { get; set; }

    public virtual Tutorprofile? Createdbytutor { get; set; }

    public virtual ClassSession? ClassSession { get; set; }
}
