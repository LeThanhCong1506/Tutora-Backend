using System;
using System.Collections.Generic;

namespace MV.DomainLayer.Entities;

public partial class Notification
{
    public int Notificationid { get; set; }

    public string? Userid { get; set; }

    public string? Title { get; set; }

    public string? Message { get; set; }

    public string? Type { get; set; }

    public string? Referenceid { get; set; }

    public bool? Isread { get; set; }

    public DateTime? Createdat { get; set; }

    public string? Channel { get; set; }

    public string? Zaborequestid { get; set; }

    public string? Deliverystatus { get; set; }

    public virtual User? User { get; set; }
}
