namespace MV.DomainLayer.DTO.FraudDetection;

public class FraudCheckResult
{
    public List<RuleResult> RuleResults { get; set; } = new();

    public bool AllPassed => !RuleResults.Any(r => !r.Passed && r.IsBlocking);

    public List<string> BlockingRules => RuleResults
        .Where(r => !r.Passed && r.IsBlocking)
        .Select(r => r.RuleName)
        .ToList();

    public List<string> FlaggedRules => RuleResults
        .Where(r => r.IsFlagged)
        .Select(r => r.RuleName)
        .ToList();

    public string? BlockingMessage => BlockingRules.Any()
        ? string.Join(" ", RuleResults
            .Where(r => !r.Passed && r.IsBlocking)
            .Select(r => r.Message))
        : null;

    public string? FlaggedMessage => FlaggedRules.Any()
        ? string.Join(" ", RuleResults
            .Where(r => r.IsFlagged)
            .Select(r => r.Message))
        : null;
}

public class RuleResult
{
    public string RuleName { get; set; } = null!;
    public bool Passed { get; set; }
    public bool IsBlocking { get; set; } = true;
    public bool IsFlagged { get; set; }
    public string? Message { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }

    public static RuleResult Pass(string ruleName, Dictionary<string, object>? metadata = null) => new()
    {
        RuleName = ruleName,
        Passed = true,
        IsBlocking = true,
        IsFlagged = false,
        Metadata = metadata
    };

    public static RuleResult Fail(string ruleName, string message, Dictionary<string, object>? metadata = null) => new()
    {
        RuleName = ruleName,
        Passed = false,
        IsBlocking = true,
        IsFlagged = false,
        Message = message,
        Metadata = metadata
    };

    public static RuleResult Flag(string ruleName, string message, Dictionary<string, object>? metadata = null) => new()
    {
        RuleName = ruleName,
        Passed = true,
        IsBlocking = false,
        IsFlagged = true,
        Message = message,
        Metadata = metadata
    };
}
