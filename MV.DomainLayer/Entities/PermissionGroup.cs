namespace MV.DomainLayer.Entities;

public class PermissionGroup
{
    public Guid PermissionGroupId { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public long Version { get; set; }
    public bool IsDeleted { get; set; }
    public string CreatedBy { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public string UpdatedBy { get; set; } = null!;
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }

    public virtual ICollection<PermissionGroupPermission> Permissions { get; set; } = new List<PermissionGroupPermission>();
    public virtual ICollection<StaffPermissionGroupAssignment> StaffAssignments { get; set; } = new List<StaffPermissionGroupAssignment>();
}
