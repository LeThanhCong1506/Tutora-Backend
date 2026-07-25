namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// Dry-run of what ProcessRefundAsync would actually do for a given percentage — same clamped
/// math, no side effects. Lets admin see the real amounts (and any funding shortfall) before
/// committing to a custom refund percentage.
/// </summary>
public class RefundPreviewResponse
{
    public int ClassSessionId { get; set; }
    public int Percentage { get; set; }
    public decimal ParentRefundAmount { get; set; }
    public decimal TutorPayoutAmount { get; set; }

    /// <summary>Has the deposit ("đợt 1" — exactly session 1's share) been paid?</summary>
    public bool IsDepositPaid { get; set; }

    /// <summary>Has the remaining amount ("đợt 2" — sessions 2..N) been paid?</summary>
    public bool IsRemainingPaid { get; set; }

    public decimal TutorFrozenBalance { get; set; }

    /// <summary>Non-empty when the raw calculated amount had to be clamped down — e.g. remaining
    /// not yet paid, or tutor's frozen balance can't cover the full per-session escrow.</summary>
    public List<string> Warnings { get; set; } = new();
}
