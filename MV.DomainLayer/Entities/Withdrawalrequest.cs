using System;
using System.Collections.Generic;

namespace MV.DomainLayer.Entities;

public partial class Withdrawalrequest
{
    public int Withdrawalid { get; set; }

    public string? Userid { get; set; }

    public decimal? Amount { get; set; }

    public string? Bankname { get; set; }

    public string? Accountnumber { get; set; }

    public string? Accountholdername { get; set; }

    public string? Status { get; set; }

    public DateTime? Requestedat { get; set; }

    public DateTime? Processedat { get; set; }

    // PayOS tracking fields
    public string? Payostransactionid { get; set; }

    public string? Payosstatus { get; set; }

    public string? Payosresponsecode { get; set; }

    public string? Payoserror { get; set; }

    public int? Retrycount { get; set; }

    public DateTime? Lastretryat { get; set; }

    public string? Decision { get; set; }

    public string? Processedby { get; set; }

    public virtual User? User { get; set; }
}
