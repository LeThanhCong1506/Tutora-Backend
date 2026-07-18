namespace MV.DomainLayer.Entities;

public class PermissionGroupPermission
{
    public Guid PermissionGroupId { get; set; }
    public string PermissionKey { get; set; } = null!;

    public virtual PermissionGroup PermissionGroup { get; set; } = null!;
    public virtual PermissionDefinition PermissionDefinition { get; set; } = null!;
}
