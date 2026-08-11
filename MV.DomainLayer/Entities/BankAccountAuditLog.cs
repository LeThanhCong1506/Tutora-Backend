using System;

namespace MV.DomainLayer.Entities;

/// <summary>Audit trail of bank_accounts create/update/delete — always preceded by OTP verification.</summary>
public partial class BankAccountAuditLog
{
    public long Bankaccountauditlogid { get; set; }

    public string Userid { get; set; } = null!;

    public int? Bankaccountid { get; set; }

    public string Action { get; set; } = null!;

    public string? Oldbankname { get; set; }

    public string? Oldaccountnumber { get; set; }

    public string? Oldaccountholdername { get; set; }

    public string? Newbankname { get; set; }

    public string? Newaccountnumber { get; set; }

    public string? Newaccountholdername { get; set; }

    public DateTime Changedat { get; set; }

    public string? Ipaddress { get; set; }

    public string? Useragent { get; set; }

    public virtual User User { get; set; } = null!;

    public virtual BankAccount? BankAccount { get; set; }
}
