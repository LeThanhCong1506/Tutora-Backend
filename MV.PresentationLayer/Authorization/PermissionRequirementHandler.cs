using Microsoft.AspNetCore.Authorization;
using MV.DomainLayer.Constants;

namespace MV.PresentationLayer.Authorization;

public sealed class PermissionRequirementHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.IsInRole(UserRole.Admin))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        // Permission claims are meaningful only for the Staff role. This prevents
        // customer roles from satisfying delegated CMS policies even if a stale or
        // incorrectly-issued token happens to contain a permission claim.
        if (context.User.IsInRole(UserRole.Staff) &&
            context.User.HasClaim(Permissions.ClaimType, requirement.PermissionKey))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
