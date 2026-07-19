namespace MV.DomainLayer.Entities;

/// <summary>Append-only audit trail for permission-group and Staff assignment changes.</summary>
public class PermissionAuditLog
{
    public long PermissionAuditLogId { get; set; }
    public string Action { get; set; } = null!;
    public string EntityType { get; set; } = null!;
    public string EntityId { get; set; } = null!;
    public Guid? PermissionGroupId { get; set; }
    public string? StaffUserId { get; set; }
    public long? Version { get; set; }
    public string ActorUserId { get; set; } = null!;
    public string DetailsJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
}
