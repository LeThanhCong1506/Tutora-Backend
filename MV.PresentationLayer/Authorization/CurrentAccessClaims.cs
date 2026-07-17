using System.Security.Claims;
using MV.DomainLayer.Constants;

namespace MV.PresentationLayer.Authorization;

/// <summary>
/// Replaces authorization claims with the current database role and group
/// permissions. It is called from JWT validation on every authenticated request,
/// so a grant, revoke, role change or account lock does not require a new JWT.
/// </summary>
public static class CurrentAccessClaims
{
    public static void Replace(ClaimsIdentity identity, string currentRole, IEnumerable<string> permissionKeys)
    {
        foreach (var roleClaim in identity.FindAll(ClaimTypes.Role).ToList())
            identity.RemoveClaim(roleClaim);
        foreach (var permissionClaim in identity.FindAll(Permissions.ClaimType).ToList())
            identity.RemoveClaim(permissionClaim);

        identity.AddClaim(new Claim(ClaimTypes.Role, currentRole));
        if (!string.Equals(currentRole, UserRole.Staff, StringComparison.OrdinalIgnoreCase))
            return;

        foreach (var key in permissionKeys
                     .Where(Permissions.All.Contains)
                     .Distinct(StringComparer.Ordinal))
        {
            identity.AddClaim(new Claim(Permissions.ClaimType, key));
        }
    }
}
