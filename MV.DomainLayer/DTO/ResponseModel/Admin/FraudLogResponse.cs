namespace MV.DomainLayer.DTO.ResponseModel.Admin;

public class FraudLogResponse
{
    public List<FraudLogItem> Items { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public class FraudLogItem
{
    public int LogId { get; set; }
    public string TutorId { get; set; } = string.Empty;
    public string TutorName { get; set; } = string.Empty;
    public int? WithdrawalRequestId { get; set; }
    public string RuleName { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public bool IsFlagged { get; set; }
    public string? Message { get; set; }
    public DateTime CheckedAt { get; set; }
}
