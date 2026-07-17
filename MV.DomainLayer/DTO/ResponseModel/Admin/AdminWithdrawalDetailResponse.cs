namespace MV.DomainLayer.DTO.ResponseModel.Admin;

public class AdminWithdrawalDetailResponse
{
    public RequestInfoResponse RequestInfo { get; set; } = new();
    public TutorInfoResponse TutorInfo { get; set; } = new();
    public List<PreviousWithdrawalResponse> PreviousWithdrawals { get; set; } = new();
    public WalletInfoResponse WalletInfo { get; set; } = new();
    public List<TimelineEventResponse> Timeline { get; set; } = new();
}

public class RequestInfoResponse
{
    public int WithdrawalId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Decision { get; set; }
    public string? BankName { get; set; }
    public string? AccountNumber { get; set; }
    public string? AccountHolderName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public string? ProcessedBy { get; set; }
    public string? CompletionNote { get; set; }
    public string? ClaimedBy { get; set; }
    public DateTime? ClaimedAt { get; set; }
    public string? RejectionReason { get; set; }
    public string? TransactionId { get; set; }
    public DateTime? PaidAt { get; set; }
    public string? ProofImageUrl { get; set; }
}

public class TutorInfoResponse
{
    public string TutorId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public int AccountAgeDays { get; set; }
    public int CompletedClassSessions { get; set; }
    public decimal TotalEarnings { get; set; }
    public DateTime JoinedAt { get; set; }
}

public class PreviousWithdrawalResponse
{
    public int WithdrawalId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; }
}

public class WalletInfoResponse
{
    public decimal Balance { get; set; }
    public decimal FrozenBalance { get; set; }
    public decimal AvailableBalance { get; set; }
    public decimal TotalBalance { get; set; }
}

public class TimelineEventResponse
{
    public DateTime Timestamp { get; set; }
    public string Event { get; set; } = string.Empty;
    public string? Details { get; set; }
}
