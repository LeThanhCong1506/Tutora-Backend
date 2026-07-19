namespace MV.DomainLayer.Entities;

/// <summary>
/// A permission key that can be assigned to a permission group.
/// The table is seeded by the managed database migration and is not editable from the CMS.
/// </summary>
public class PermissionDefinition
{
    public string Key { get; set; } = null!;
    public string Domain { get; set; } = null!;
    public string Module { get; set; } = null!;
    public string Action { get; set; } = null!;
    public string Label { get; set; } = null!;
    public virtual ICollection<PermissionGroupPermission> PermissionGroups { get; set; } = new List<PermissionGroupPermission>();
    public virtual ICollection<PermissionDefinitionRequirement> Requirements { get; set; } = new List<PermissionDefinitionRequirement>();
}
