namespace MV.DomainLayer.DTO.BankVerification;

public record RequestVerifyRequest(string BankCode, string AccountNumber);

public record RequestVerifyResponse
{
    public bool IsSuccess { get; init; }
    // SECURITY: TransferCode removed - never expose verification code to client
    public DateTime? ExpiresAt { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ErrorCode { get; init; }
    public string? Message { get; init; }
}

public record ConfirmVerifyRequest(string VerificationCode);

public record ConfirmVerifyResponse
{
    public bool IsVerified { get; init; }
    public DateTime? VerifiedAt { get; init; }
    public int AttemptsLeft { get; init; }
    public string? Message { get; init; }
    public string? ErrorMessage { get; init; }
}

public record BankVerificationStatusResponse
{
    public bool IsVerified { get; init; }
    public bool IsPending { get; init; }
    public bool IsReadyToConfirm { get; init; }
    public int AttemptsLeft { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? BankName { get; init; }
    public string? MaskedAccountNumber { get; init; }
}

public record BankHubWebhookPayload
{
    public string TransactionId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? Reference { get; init; }
    public decimal Amount { get; init; }
    public DateTime? CompletedAt { get; init; }
}

public record BankInfoResponse
{
    public string Code { get; init; } = string.Empty;
    public string ShortName { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string? LogoUrl { get; init; }
    public string? Bin { get; init; }
    public bool SupportsInstantTransfer { get; init; }
}
