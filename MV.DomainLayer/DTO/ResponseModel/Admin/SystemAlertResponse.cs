namespace MV.DomainLayer.DTO.ResponseModel.Admin;

public class SystemAlertResponse
{
    public List<SystemAlertItem> Items { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public class SystemAlertItem
{
    public int AlertId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Metadata { get; set; }
    public bool Resolved { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? ResolvedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}
