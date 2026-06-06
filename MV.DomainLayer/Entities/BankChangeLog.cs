namespace MV.DomainLayer.Entities;

public class BankChangeLog
{
    public int Logid { get; set; }
    public string Tutorid { get; set; } = null!;
    public string? Oldbankname { get; set; }
    public string? Oldbankaccountnumber { get; set; }
    public string? Oldbankaccountname { get; set; }
    public string? Newbankname { get; set; }
    public string? Newbankaccountnumber { get; set; }
    public string? Newbankaccountname { get; set; }
    public DateTime Changedat { get; set; }
    public string? Ipaddress { get; set; }
    public string? Useragent { get; set; }
    public string? Reason { get; set; }

    public User? Tutor { get; set; }
}
