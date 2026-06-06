using System;
using System.Collections.Generic;

namespace MV.DomainLayer.Entities;

public partial class Promotion
{
    public int Promotionid { get; set; }

    public string Code { get; set; } = null!;

    public string? Description { get; set; }

    public string? Discounttype { get; set; }

    public decimal? Discountvalue { get; set; }

    public decimal? Maxdiscountamount { get; set; }

    public decimal? Minordervalue { get; set; }

    public DateTime? Startdate { get; set; }

    public DateTime? Enddate { get; set; }

    public int? Usagelimit { get; set; }

    public int? Usagecount { get; set; }

    public bool? Isactive { get; set; }

    public DateTime? Createdat { get; set; }

    public virtual ICollection<Booking> Bookings { get; set; } = new List<Booking>();
}
