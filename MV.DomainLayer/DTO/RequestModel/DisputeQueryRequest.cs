namespace MV.DomainLayer.DTO.RequestModel;

/// <summary>
/// Query parameters for dispute list
/// </summary>
public class DisputeQueryRequest
{
    /// <summary>
    /// Filter by status (pending, investigating, resolved, closed)
    /// </summary>
    public string? Status { get; set; }

    /// <summary>
    /// Filter by dispute type (no_show, quality, payment, other)
    /// </summary>
    public string? DisputeType { get; set; }

    /// <summary>
    /// Start date for date range filter
    /// </summary>
    public DateTime? StartDate { get; set; }

    /// <summary>
    /// End date for date range filter
    /// </summary>
    public DateTime? EndDate { get; set; }

    /// <summary>
    /// Page number (1-indexed)
    /// </summary>
    public int Page { get; set; } = 1;

    /// <summary>
    /// Page size (default 10, max 50)
    /// </summary>
    public int PageSize { get; set; } = 10;
}


