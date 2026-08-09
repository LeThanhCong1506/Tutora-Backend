namespace MV.DomainLayer.Entities;

/// <summary>Đề xuất dời một buổi học sang giờ khác, do gia sư hoặc bên học chủ động tạo.</summary>
public class ClassSessionRescheduleProposal
{
    public int Rescheduleproposalid { get; set; }
    public int Classsessionid { get; set; }
    public string Proposedbyuserid { get; set; } = null!;
    public string Proposedbyrole { get; set; } = null!;
    public string Counterpartuserid { get; set; } = null!;
    public string Counterpartrole { get; set; } = null!;
    public DateTime Originalscheduledstart { get; set; }
    public DateTime Originalscheduledend { get; set; }
    public DateTime Proposedscheduledstart { get; set; }
    public DateTime Proposedscheduledend { get; set; }
    public string? Reason { get; set; }
    public string Status { get; set; } = null!;
    public DateTime Requestedat { get; set; }
    public DateTime Expiresat { get; set; }
    public DateTime? Respondedat { get; set; }
    public string? Respondedby { get; set; }
    public DateTime Createdat { get; set; }
    public DateTime Updatedat { get; set; }
    public virtual ClassSession ClassSession { get; set; } = null!;
}
