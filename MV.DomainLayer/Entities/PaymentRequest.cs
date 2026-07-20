using System;
using System.Collections.Generic;

namespace MV.DomainLayer.Entities;

public partial class PaymentRequest
{
    public int Paymentrequestid { get; set; }

    public int Bookingid { get; set; }

    public string? Userid { get; set; }

    public string Provider { get; set; } = null!;

    public string Phase { get; set; } = null!;

    public long? Ordercode { get; set; }

    public string? Paymentlinkid { get; set; }

    public decimal? Amount { get; set; }

    public string Currency { get; set; } = null!;

    public string Status { get; set; } = null!;

    public string? Destinationbankbin { get; set; }

    public string? Destinationbankname { get; set; }

    public string? Displayaccountnumber { get; set; }

    public string? Displayaccountname { get; set; }

    public string? Description { get; set; }

    public string? Checkouturl { get; set; }

    public string? Qrcode { get; set; }

    public DateTime? Expiresat { get; set; }

    public string? Providerpayload { get; set; }

    public DateTime Createdat { get; set; }

    public DateTime Updatedat { get; set; }

    public virtual Booking Booking { get; set; } = null!;

    public virtual User? User { get; set; }

    public virtual ICollection<PaymentTransaction> Paymenttransactions { get; set; } = new List<PaymentTransaction>();
}
