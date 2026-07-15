using System;

namespace MV.DomainLayer.Entities;

public partial class BankAccount
{
    public int Bankaccountid { get; set; }

    public string Userid { get; set; } = null!;

    public string? Bankname { get; set; }

    public string? Accountnumber { get; set; }

    public string? Accountholdername { get; set; }

    public DateTime? Createdat { get; set; }

    public DateTime? Updatedat { get; set; }

    public virtual User User { get; set; } = null!;
}
