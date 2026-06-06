namespace MV.DomainLayer.Entities;

public class WithdrawalScore
{
    public int Scoreid { get; set; }
    public int Withdrawalrequestid { get; set; }
    public string Tutorid { get; set; } = null!;
    public int Basescore { get; set; }
    public string? Positivefactors { get; set; }
    public string? Negativefactors { get; set; }
    public string? Fraudflags { get; set; }
    public int Totalscore { get; set; }
    public string Decision { get; set; } = null!;
    public DateTime Createdat { get; set; }

    // Navigation properties
    public virtual Withdrawalrequest? Withdrawalrequest { get; set; }
    public virtual Tutorprofile? Tutor { get; set; }
}
