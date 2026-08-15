using System;
using System.Collections.Generic;

namespace MV.DomainLayer.Entities;

/// <summary>
/// One continuous Admin/Staff &lt;-&gt; user support conversation (Tutor, Parent, or Student).
/// One thread per user for the platform's lifetime — unlike disputes, this isn't scoped to a
/// specific booking/incident, so there's nothing to key it on besides the user.
/// </summary>
public partial class Supportthread
{
    public int Supportthreadid { get; set; }

    public string Userid { get; set; } = null!;

    public int Unreadforadmin { get; set; }

    public int Unreadforuser { get; set; }

    public DateTime? Lastmessageat { get; set; }

    public DateTime? Createdat { get; set; }

    public virtual User? User { get; set; }

    public virtual ICollection<Supportmessage> Supportmessages { get; set; } = new List<Supportmessage>();
}
