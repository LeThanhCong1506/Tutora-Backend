using Microsoft.AspNetCore.Authorization;

namespace MV.PresentationLayer.Authorization;

public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public string PermissionKey { get; }

    public PermissionRequirement(string permissionKey)
    {
        PermissionKey = permissionKey;
    }
}
