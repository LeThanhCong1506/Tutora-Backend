namespace MV.DomainLayer.DTO.ResponseModel;

public class ClassSessionRescheduleProposalResponse
{
    public int RescheduleProposalId { get; set; }
    public int ClassSessionId { get; set; }
    public string ProposedByUserId { get; set; } = string.Empty;
    public string ProposedByRole { get; set; } = string.Empty;
    public string? ProposedByName { get; set; }
    public string CounterpartUserId { get; set; } = string.Empty;
    public string CounterpartRole { get; set; } = string.Empty;
    public string? CounterpartName { get; set; }
    public DateTime OriginalScheduledStart { get; set; }
    public DateTime OriginalScheduledEnd { get; set; }
    public DateTime ProposedScheduledStart { get; set; }
    public DateTime ProposedScheduledEnd { get; set; }
    public string? Reason { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RespondedAt { get; set; }
}
