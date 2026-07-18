using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using MV.DomainLayer.Constants;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer.DBContext;
using MV.PresentationLayer.Authorization;
using Xunit;

namespace MV.ApplicationLayer.Tests;

public class PermissionAuthorizationTests
{
    [Theory]
    [InlineData(UserRole.Admin, null, true)]
    [InlineData(UserRole.Staff, Permissions.UserView, true)]
    [InlineData(UserRole.Staff, null, false)]
    [InlineData(UserRole.Parent, Permissions.UserView, false)]
    public async Task PermissionHandler_EnforcesAdminStaffCustomerMatrix(
        string role, string? grantedPermission, bool expected)
    {
        var claims = new List<Claim> { new(ClaimTypes.Role, role) };
        if (grantedPermission != null)
            claims.Add(new Claim(Permissions.ClaimType, grantedPermission));
        var principal = Principal(claims);
        var requirement = new PermissionRequirement(Permissions.UserView);
        var context = new AuthorizationHandlerContext(new[] { requirement }, principal, null);

        await new PermissionRequirementHandler().HandleAsync(context);

        Assert.Equal(expected, context.HasSucceeded);
    }

    [Fact]
    public async Task PermissionHandler_RejectsAnonymous()
    {
        var requirement = new PermissionRequirement(Permissions.UserView);
        var context = new AuthorizationHandlerContext(
            new[] { requirement }, new ClaimsPrincipal(new ClaimsIdentity()), null);

        await new PermissionRequirementHandler().HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public void SameJwtPrincipal_ReflectsLiveGrantRevokeAndRoleChange()
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, UserRole.Admin),
            new Claim(Permissions.ClaimType, Permissions.PayoutApprove)
        }, "test", ClaimTypes.Name, ClaimTypes.Role);

        CurrentAccessClaims.Replace(identity, UserRole.Staff,
            new[] { Permissions.PayoutView, Permissions.PayoutApprove, "stale.unknown" });
        Assert.True(new ClaimsPrincipal(identity).IsInRole(UserRole.Staff));
        Assert.False(new ClaimsPrincipal(identity).IsInRole(UserRole.Admin));
        Assert.Contains(identity.Claims, claim => claim.Type == Permissions.ClaimType
            && claim.Value == Permissions.PayoutApprove);
        Assert.DoesNotContain(identity.Claims, claim => claim.Value == "stale.unknown");

        CurrentAccessClaims.Replace(identity, UserRole.Staff, Array.Empty<string>());
        Assert.DoesNotContain(identity.Claims, claim => claim.Type == Permissions.ClaimType);
    }

    [Fact]
    public void StaffAssignment_ModelAllowsExactlyOneGroupAndUsesConcurrencyVersion()
    {
        var options = new DbContextOptionsBuilder<AgoraDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=model_only;Username=model_only;Password=model_only",
                npgsql => npgsql.UseVector())
            .Options;

        using var context = new AgoraDbContext(options);
        var entity = context.Model.FindEntityType(typeof(StaffPermissionGroupAssignment))!;
        var primaryKey = entity.FindPrimaryKey()!;
        var version = entity.FindProperty(nameof(StaffPermissionGroupAssignment.Version));

        Assert.Single(primaryKey.Properties);
        Assert.Equal(nameof(StaffPermissionGroupAssignment.StaffUserId), primaryKey.Properties[0].Name);
        Assert.NotNull(version);
        Assert.True(version!.IsConcurrencyToken);
    }

    private static ClaimsPrincipal Principal(IEnumerable<Claim> claims) =>
        new(new ClaimsIdentity(claims, "test", ClaimTypes.Name, ClaimTypes.Role));
}
