namespace MV.DomainLayer.Entities;

public class PermissionDefinitionRequirement
{
    public string PermissionKey { get; set; } = null!;
    public string RequiredPermissionKey { get; set; } = null!;

    public virtual PermissionDefinition PermissionDefinition { get; set; } = null!;
    public virtual PermissionDefinition RequiredPermissionDefinition { get; set; } = null!;
}
