namespace MV.DomainLayer.DTO.TrustScoring;

public class ScoreFactor
{
    public string Factor { get; set; } = null!;
    public int Points { get; set; }
    public string Detail { get; set; } = null!;
}

public class TrustScoreResult
{
    public int BaseScore { get; set; }
    public List<ScoreFactor> PositiveFactors { get; set; } = new();
    public List<ScoreFactor> NegativeFactors { get; set; } = new();
    public List<string> FraudFlags { get; set; } = new();
    public int TotalScore { get; set; }

    public int TotalPositivePoints => PositiveFactors.Sum(f => f.Points);
    public int TotalNegativePoints => NegativeFactors.Sum(f => f.Points);
}

public class ApprovalDecision
{
    public string Decision { get; set; } = null!;
    public string Message { get; set; } = null!;
    public int EstimatedProcessingHours { get; set; }
    public bool RequiresAdmin { get; set; }
    public string? Reason { get; set; }
}
