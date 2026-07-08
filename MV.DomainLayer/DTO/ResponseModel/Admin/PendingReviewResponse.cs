namespace MV.DomainLayer.DTO.ResponseModel.Admin;

public class PendingReviewResponse
{
    public List<PendingReviewItem> Items { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public class PendingReviewItem
{
    public int WithdrawalId { get; set; }
    public string TutorId { get; set; } = string.Empty;
    public string TutorName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime RequestedAt { get; set; }
}
