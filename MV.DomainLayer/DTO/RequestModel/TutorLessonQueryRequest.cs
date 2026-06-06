namespace MV.DomainLayer.DTO.RequestModel;

/// <summary>
/// Query parameters for tutor lesson list
/// </summary>
public class TutorLessonQueryRequest
{
    /// <summary>
    /// Filter by status (scheduled, in_progress, pending_confirmation, completed, cancelled, disputed)
    /// </summary>
    public string? Status { get; set; }

    /// <summary>
    /// Filter by student ID
    /// </summary>
    public string? StudentId { get; set; }

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

    /// <summary>
    /// Sort by field (scheduledStart, createdAt)
    /// </summary>
    public string SortBy { get; set; } = "scheduledStart";

    /// <summary>
    /// Sort direction (asc, desc)
    /// </summary>
    public string SortDirection { get; set; } = "asc";
}
