namespace MV.DomainLayer.Entities;

/// <summary>Audit trail for a class session that starts outside its booked schedule.</summary>
public class ClassSessionScheduleChange
{
    public int Schedulechangeid { get; set; }
    public int Classsessionid { get; set; }
    public DateTime Originalscheduledstart { get; set; }
    public DateTime Originalscheduledend { get; set; }
    public string Tutoruserid { get; set; } = null!;
    public string Learnerapproveruserid { get; set; } = null!;
    public string Learnerapproverrole { get; set; } = null!;
    public DateTime? Tutorconfirmedat { get; set; }
    public string? Tutorconfirmedby { get; set; }
    public DateTime? Learnerconfirmedat { get; set; }
    public string? Learnerconfirmedby { get; set; }
    public DateTime? Rejectedat { get; set; }
    public string? Rejectedby { get; set; }
    public DateTime Requestedat { get; set; }
    public DateTime Expiresat { get; set; }
    public DateTime? Approvedat { get; set; }
    public DateTime? Appliedat { get; set; }
    public DateTime? Adjustedscheduledstart { get; set; }
    public DateTime? Adjustedscheduledend { get; set; }
    public string Status { get; set; } = null!;
    public DateTime Createdat { get; set; }
    public DateTime Updatedat { get; set; }
    public virtual ClassSession ClassSession { get; set; } = null!;
}
