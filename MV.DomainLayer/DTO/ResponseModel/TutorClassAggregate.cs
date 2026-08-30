namespace MV.DomainLayer.DTO.ResponseModel;

public class TutorClassAggregate
{
    public int BookingId { get; set; }
    public string? SubjectName { get; set; }
    public string? StudentName { get; set; }
    public int TotalSessions { get; set; }
    public int CompletedSessions { get; set; }
    public int ActiveSessions { get; set; }
    public bool HasInProgress { get; set; }
    public bool HasPending { get; set; }

    /// <summary>True if any session is still neither completed nor cancelled.</summary>
    public bool HasNonTerminal { get; set; }

    /// <summary>
    /// True khi còn buổi ở trạng thái <c>reserved</c>
    /// </summary>
    public bool HasReserved { get; set; }

    public DateTime? NextSessionStart { get; set; }
    public DateTime LatestStart { get; set; }
    public string? Schedule { get; set; }
}
