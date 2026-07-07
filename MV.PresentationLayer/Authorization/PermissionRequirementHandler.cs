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

        if (context.User.HasClaim(Permissions.ClaimType, requirement.PermissionKey))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
