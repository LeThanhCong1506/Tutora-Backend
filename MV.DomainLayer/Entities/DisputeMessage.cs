using System;

namespace MV.DomainLayer.Entities;

/// <summary>
/// A message in one of a dispute's two private admin-mediated threads (see
/// <see cref="MV.DomainLayer.Constants.DisputeThreadType"/>) — admin&lt;-&gt;tutor and
/// admin&lt;-&gt;parent/student, kept separate so neither party sees the other's conversation.
/// </summary>
public partial class DisputeMessage
{
    public int Disputemessageid { get; set; }

    public int Disputeid { get; set; }

    public string Threadtype { get; set; } = null!;

    public string? Senderid { get; set; }

    public string? Senderrole { get; set; }

    public string Message { get; set; } = null!;

    public DateTime? Createdat { get; set; }

    public virtual Dispute? Dispute { get; set; }

    public virtual User? SenderidNavigation { get; set; }
}
