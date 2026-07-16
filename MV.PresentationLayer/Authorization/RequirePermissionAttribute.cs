using Microsoft.AspNetCore.Authorization;

namespace MV.PresentationLayer.Authorization;

/// <summary>
/// Requires the caller to hold the given permission key, or to be Admin (superuser bypass).
/// See <see cref="PermissionRequirementHandler"/> for the enforcement logic.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public sealed class RequirePermissionAttribute : AuthorizeAttribute, IAuthorizationRequirementData
{
    public string PermissionKey { get; }

    public RequirePermissionAttribute(string permissionKey)
    {
        PermissionKey = permissionKey;
    }

    public IEnumerable<IAuthorizationRequirement> GetRequirements()
    {
        yield return new PermissionRequirement(PermissionKey);
    }
}
