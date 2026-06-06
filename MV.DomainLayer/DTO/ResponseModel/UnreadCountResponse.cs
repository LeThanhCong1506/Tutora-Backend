namespace MV.DomainLayer.DTO.ResponseModel;

public class UnreadCountResponse
{
    public int UnreadCount { get; set; }

    /// <summary>
    /// Unread notification counts grouped by non-null notification type.
    /// Legacy notifications with null/empty type are excluded from this map.
    /// </summary>
    public Dictionary<string, int> ByType { get; set; } = new();

    public int Total { get; set; }
}
