using System;

namespace MV.DomainLayer.Entities;

/// <summary>
/// A single message in a <see cref="Supportthread"/>. <see cref="Senderside"/> (see
/// <see cref="MV.DomainLayer.Constants.SupportSenderSide"/>) says which side of the conversation
/// sent it; <see cref="Senderid"/> is still the actual staff/user id for auditing.
/// </summary>
public partial class Supportmessage
{
    public int Supportmessageid { get; set; }

    public int Supportthreadid { get; set; }

    public string? Senderid { get; set; }

    public string Senderside { get; set; } = null!;

    /// <summary>"text" | "image" — see <see cref="MV.DomainLayer.Constants.ChatMessageType"/> (reused, not duplicated).</summary>
    public string Messagetype { get; set; } = null!;

    /// <summary>The text content, or the image URL when <see cref="Messagetype"/> is "image".</summary>
    public string Message { get; set; } = null!;

    public DateTime? Createdat { get; set; }

    public virtual Supportthread? Supportthread { get; set; }

    public virtual User? SenderidNavigation { get; set; }
}
