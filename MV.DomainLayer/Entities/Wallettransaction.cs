using System;
using System.Collections.Generic;

namespace MV.DomainLayer.Entities;

public partial class Wallettransaction
{
    public int Transactionid { get; set; }

    public int? Walletid { get; set; }

    public decimal? Amount { get; set; }

    public string? Transactiontype { get; set; }

    public int? Referenceid { get; set; }

    public string? Referencetable { get; set; }

    public string? Description { get; set; }

    public DateTime? Createdat { get; set; }

    public long? Ordercode { get; set; }

    public virtual Wallet? Wallet { get; set; }
}
