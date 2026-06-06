using System;
using System.Collections.Generic;

namespace MV.DomainLayer.Entities;

public partial class Chatchannel
{
    public int Channelid { get; set; }

    public int? Bookingid { get; set; }

    public string? Parentid { get; set; }

    public string? Tutorid { get; set; }

    // Phase 4: Uncomment khi đã chạy SQL: ALTER TABLE chatchannels ADD COLUMN studentid varchar(50);
    public string? Studentid { get; set; }

    public DateTime? Createdat { get; set; }

    public DateTime? Lastmessageat { get; set; }

    public string? Status { get; set; }

    public virtual Booking? Booking { get; set; }

    public virtual User? Parent { get; set; }

    public virtual User? Tutor { get; set; }

    // Phase 4: navigation cho studentid
    public virtual User? Student { get; set; }

    public virtual ICollection<Chatmessage> Chatmessages { get; set; } = new List<Chatmessage>();

    public virtual ICollection<User> Users { get; set; } = new List<User>();
}
