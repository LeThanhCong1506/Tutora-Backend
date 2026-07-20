namespace MV.DomainLayer.Entities;

/// <summary>
/// The single permission-group slot of a Staff account. A null group means the
/// account currently has no delegated permissions; the row is retained so its
/// concurrency version remains monotonic across assign/unassign operations.
/// </summary>
public class StaffPermissionGroupAssignment
{
    public string StaffUserId { get; set; } = null!;
    public Guid? PermissionGroupId { get; set; }
    public long Version { get; set; }
    public string UpdatedBy { get; set; } = null!;
    public DateTime UpdatedAt { get; set; }

    public virtual User StaffUser { get; set; } = null!;
    public virtual PermissionGroup? PermissionGroup { get; set; }
}
