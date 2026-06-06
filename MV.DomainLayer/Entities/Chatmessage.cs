using System;
using System.Collections.Generic;

namespace MV.DomainLayer.Entities;

public partial class Chatmessage
{
    public int Messageid { get; set; }

    public int? Channelid { get; set; }

    public string? Senderid { get; set; }

    public string? Content { get; set; }

    public string? Messagetype { get; set; }

    public DateTime? Createdat { get; set; }

    public string? Metadata { get; set; }

    public bool? Isread { get; set; }

    public DateTime? Readat { get; set; }

    public virtual Chatchannel? Channel { get; set; }

    public virtual User? Sender { get; set; }
}
