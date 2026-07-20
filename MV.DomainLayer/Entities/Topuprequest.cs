using System;
using System.Collections.Generic;

namespace MV.DomainLayer.Entities;

public partial class Topuprequest
{
    public int Topuprequestid { get; set; }

    public int? Bookingid { get; set; }

    public string? Paymentphase { get; set; }

    public long Ordercode { get; set; }

    public string? Userid { get; set; }

    public decimal? Amount { get; set; }

    public string? Status { get; set; }

    public string? Paymentlinkid { get; set; }

    public DateTime? Createdat { get; set; }

    public DateTime? Completedat { get; set; }

    public DateTime? Expiresat { get; set; }

    public virtual User? User { get; set; }
}
