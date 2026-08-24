namespace MV.DomainLayer.DTO.ResponseModel.Admin;

/// <summary>
/// Full detail of a single booking for admin review.
/// </summary>
public class AdminBookingDetailResponse
{
    public int BookingId { get; set; }
    public string? Status { get; set; }
    public string? TeachingMode { get; set; }
    public string? PaymentCode { get; set; }

    /// <summary>
    /// Composite address string (detail, ward, district, city). Null when TeachingMode = online.
    /// </summary>
    public string? Location { get; set; }

    // ── Parties ─────────────────────────────────────────────────────────────
    public AdminBookingPartyInfo Tutor { get; set; } = new();
    public AdminBookingPartyInfo? Parent { get; set; }
    public AdminBookingStudentInfo? Student { get; set; }
    public AdminBookingSubjectInfo? Subject { get; set; }

    // ── Grouped sections ─────────────────────────────────────────────────────
    public AdminBookingFinancials Financials { get; set; } = new();
    public AdminBookingProgress Progress { get; set; } = new();
    public AdminBookingCancellation Cancellation { get; set; } = new();

    // ── Dates ────────────────────────────────────────────────────────────────
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    // ── New fields ───────────────────────────────────────────────────────────
    public List<BookingTimelineEvent> Timeline { get; set; } = new();
    public List<AdminBookingClassSessionItem> ClassSessions { get; set; } = new();
    public AdminBookingPaymentBreakdown PaymentBreakdown { get; set; } = new();

    /// <summary>Lịch sử từng dòng Wallettransaction gắn với booking này (mới nhất trước).</summary>
    public List<AdminBookingWalletTransactionItem> Transactions { get; set; } = new();
}

public class AdminBookingPartyInfo
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? AvatarUrl { get; set; }
    public string? Email { get; set; }
}

public class AdminBookingStudentInfo
{
    public string? Id { get; set; }
    public string? Name { get; set; }

    /// <summary>
    /// Avatar of the linked user account when the student has one, otherwise the
    /// copy stored on the student profile. Null when neither has an avatar.
    /// </summary>
    public string? AvatarUrl { get; set; }

    public string? GradeLevel { get; set; }
}

public class AdminBookingSubjectInfo
{
    public int? Id { get; set; }
    public string? Name { get; set; }
}

public class AdminBookingFinancials
{
    public decimal? Price { get; set; }
    public decimal? DiscountApplied { get; set; }
    public decimal? FinalPrice { get; set; }
    public string? PaymentStatus { get; set; }
}

public class AdminBookingProgress
{
    public int? TotalSessions { get; set; }
    public int CompletedSessions { get; set; }
    public DateTime? StartDate { get; set; }
}

public class AdminBookingCancellation
{
    public bool IsCancelled { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? Reason { get; set; }
}

// ── Timeline ─────────────────────────────────────────────────────────────────

public class BookingTimelineEvent
{
    /// <summary>Human-readable label for this event, e.g. "Booking created"</summary>
    public string Label { get; set; } = string.Empty;
    /// <summary>Booking status at this point in time</summary>
    public string? Status { get; set; }
    public DateTime OccurredAt { get; set; }
}

// ── ClassSessions ───────────────────────────────────────────────────────────────────

public class AdminBookingClassSessionItem
{
    public int ClassSessionId { get; set; }
    public int ClassSessionNumber { get; set; }
    public string? Status { get; set; }
    public DateTime ScheduledStart { get; set; }
    public DateTime ScheduledEnd { get; set; }
    public DateTime? RealStart { get; set; }
    public DateTime? RealEnd { get; set; }
    public decimal? ClassSessionPrice { get; set; }
    public bool? IsSettled { get; set; }
    public bool? IsMakeup { get; set; }
    /// <summary>Buổi phụ (Link 2) — sinh ra khi <see cref="OriginalClassSessionId"/> bị báo ngắt giữa chừng.</summary>
    public bool? IsContinuation { get; set; }
    /// <summary>Buổi học lại (Link 3) — sinh ra khi hoà giải dispute chọn "học lại".</summary>
    public bool? IsDisputeRelearn { get; set; }
    /// <summary>Buổi gốc mà buổi bù/buổi phụ/buổi học lại này trỏ về — null nếu đây là buổi gốc.</summary>
    public int? OriginalClassSessionId { get; set; }
    public string? TutorNotes { get; set; }
    public string? MeetingLink { get; set; }
}

// ── Payment Breakdown ─────────────────────────────────────────────────────────

public class AdminBookingPaymentBreakdown
{
    public decimal? OriginalPrice { get; set; }
    public decimal? DiscountApplied { get; set; }
    public decimal? FinalPrice { get; set; }
    public decimal? DepositAmount { get; set; }
    public DateTime? DepositPaidAt { get; set; }
    public decimal? RemainingAmount { get; set; }
    public DateTime? RemainingPaidAt { get; set; }
    public decimal? PlatformFee { get; set; }
    public decimal? TutorFee { get; set; }
    public decimal? ParentFee { get; set; }
    public string? EscrowStatus { get; set; }
    public decimal? RefundAmount { get; set; }
    public string? RefundStatus { get; set; }
    public string? PaymentStatus { get; set; }
    public DateTime? PaymentDueAt { get; set; }
}

// ── Wallet transaction history ────────────────────────────────────────────────

public class AdminBookingWalletTransactionItem
{
    public int TransactionId { get; set; }

    /// <summary>Deposit / DepositPayment / RemainingPayment / EscrowCredit / EscrowRelease / EscrowReversal / Refund / ...</summary>
    public string? TransactionType { get; set; }

    /// <summary>Số dương = tiền vào ví; số âm = tiền bị rút khỏi ví (vd EscrowReversal).</summary>
    public decimal? Amount { get; set; }
    public string? Description { get; set; }
    public DateTime? CreatedAt { get; set; }

    /// <summary>"tutor" | "parent" | "student" | null (không xác định được chủ ví).</summary>
    public string? WalletOwnerRole { get; set; }
    public string? WalletOwnerName { get; set; }
}
