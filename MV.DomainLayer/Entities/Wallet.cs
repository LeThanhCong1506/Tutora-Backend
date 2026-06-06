using System;
using System.Collections.Generic;

namespace MV.DomainLayer.Entities;

public partial class Wallet
{
    public int Walletid { get; set; }

    public string? Userid { get; set; }

    public decimal? Balance { get; set; }

    public decimal? Frozenbalance { get; set; }

    public DateTime? Lastupdated { get; set; }

    public virtual User? User { get; set; }

    public virtual ICollection<Wallettransaction> Wallettransactions { get; set; } = new List<Wallettransaction>();
}
