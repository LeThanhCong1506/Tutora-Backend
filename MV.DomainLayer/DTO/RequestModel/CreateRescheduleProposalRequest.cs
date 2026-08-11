namespace MV.DomainLayer.DTO.RequestModel;

public sealed class CreateRescheduleProposalRequest
{
    public DateTime ProposedScheduledStart { get; set; }
    public string? Reason { get; set; }
}
