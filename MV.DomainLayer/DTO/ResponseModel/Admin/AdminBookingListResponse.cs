namespace MV.DomainLayer.DTO.ResponseModel.Admin;

/// <summary>
/// Paged wrapper for GET /api/admin/bookings
/// </summary>
public class AdminBookingListResponse
{
    public List<AdminBookingListItem> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

/// <summary>
/// One row in the admin booking list — enough for scanning without opening detail.
/// </summary>
public class AdminBookingListItem
{
    public int BookingId { get; set; }
    public string? Status { get; set; }
    public string? PaymentStatus { get; set; }
    public string? TeachingMode { get; set; }
    public string? PaymentCode { get; set; }

    // ── Parties ─────────────────────────────────────────────────────────────
    public string? TutorId { get; set; }
    public string? TutorName { get; set; }
    public string? TutorAvatarUrl { get; set; }

    public string? ParentId { get; set; }
    public string? ParentName { get; set; }
    public string? ParentEmail { get; set; }

    public string? StudentId { get; set; }
    public string? StudentName { get; set; }
    public string? GradeLevel { get; set; }

    // ── Subject ──────────────────────────────────────────────────────────────
    public int? SubjectId { get; set; }
    public string? SubjectName { get; set; }

    // ── Financial summary ────────────────────────────────────────────────────
    public decimal? Price { get; set; }
    public decimal? DiscountApplied { get; set; }
    public decimal? FinalPrice { get; set; }
    public decimal? PlatformFee { get; set; }

    // ── Progress ─────────────────────────────────────────────────────────────
    public int? SessionCount { get; set; }
    public int? SessionsRemaining { get; set; }
    public int ClassSessionsCompleted { get; set; }
    public int ClassSessionsTotal { get; set; }

    // ── Dates ────────────────────────────────────────────────────────────────
    public DateTime? StartDate { get; set; }
    public DateTime? CreatedAt { get; set; }

    // ── Cancellation (chỉ có giá trị khi Status = cancelled) ─────────────────
    public DateTime? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }
}
