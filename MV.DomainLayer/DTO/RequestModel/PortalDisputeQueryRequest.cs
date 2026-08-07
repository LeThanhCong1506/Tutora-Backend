namespace MV.DomainLayer.DTO.RequestModel;

/// <summary>
/// Query parameters for a signed-in user's own dispute list.
/// Shared by the tutor and parent/student portal endpoints.
/// </summary>
public sealed class PortalDisputeQueryRequest
{
    /// <summary>
    /// Filter by status (pending, investigating, confirmed_no_show, resolved, closed).
    /// </summary>
    public string? Status { get; set; }

    /// <summary>
    /// Filter by dispute type (no_show, quality, payment, other).
    /// </summary>
    public string? DisputeType { get; set; }

    /// <summary>
    /// Search by dispute, booking, or class-session id, or by text in the dispute reason.
    /// Displayed identifiers such as "#11" and "Booking #159" are accepted.
    /// </summary>
    public string? Search { get; set; }

    /// <summary>
    /// Order by creation time: "asc" = oldest first, "desc" (default) = newest first.
    /// </summary>
    public string? SortDirection { get; set; }

    /// <summary>
    /// Page number (1-indexed).
    /// </summary>
    public int Page { get; set; } = 1;

    /// <summary>
    /// Page size (default 10, max 50).
    /// </summary>
    public int PageSize { get; set; } = 10;
}
