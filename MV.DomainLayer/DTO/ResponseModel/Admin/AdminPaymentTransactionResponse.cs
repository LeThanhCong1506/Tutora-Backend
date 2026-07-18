using System.Text.Json;

namespace MV.DomainLayer.DTO.ResponseModel.Admin;

/// <summary>
/// Paginated payment transaction audit. The wallet balance ledger is exposed
/// separately by <c>/api/admin/financials/transactions</c>.
/// </summary>
public class AdminPaymentTransactionListResponse
{
    public List<AdminPaymentTransactionItem> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public decimal TotalInboundAmount { get; set; }
    public decimal TotalOutboundAmount { get; set; }
}

public class AdminPaymentTransactionItem
{
    public int PaymentTransactionId { get; set; }

    public string? UserId { get; set; }
    public string? UserFullName { get; set; }
    public string? UserEmail { get; set; }
    public string? UserRole { get; set; }

    public string PaymentMethod { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? CaptureSource { get; set; }
    public string? ReconciliationStatus { get; set; }
    public string? CaptureFingerprint { get; set; }

    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public long? OrderCode { get; set; }
    public string? ProviderTransactionId { get; set; }
    public string? PaymentLinkId { get; set; }

    public int? PaymentRequestId { get; set; }
    public int? BookingId { get; set; }
    public int? WithdrawalId { get; set; }

    public string? Description { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime? CreatedAt { get; set; }
    public string? ProcessedBy { get; set; }
    public string? ProcessedByName { get; set; }
    public string? Note { get; set; }
}

public class AdminPaymentTransactionDetailResponse : AdminPaymentTransactionItem
{
    public string? SourceAccountBankId { get; set; }
    public string? SourceAccountBankName { get; set; }
    public string? SourceAccountNumber { get; set; }
    public string? SourceAccountName { get; set; }

    public string? DestinationAccountBankBin { get; set; }
    public string? DestinationAccountBankName { get; set; }
    public string? DestinationAccountNumber { get; set; }
    public string? DestinationAccountName { get; set; }
    public string? DestinationVirtualAccountNumber { get; set; }
    public string? DestinationVirtualAccountName { get; set; }

    public string? WebhookCode { get; set; }
    public string? WebhookDescription { get; set; }
    public bool? WebhookSuccess { get; set; }
    public string? ProviderCode { get; set; }
    public string? ProviderDescription { get; set; }

    public JsonElement? ProviderPayload { get; set; }
    public JsonElement? WebhookPayload { get; set; }
}
