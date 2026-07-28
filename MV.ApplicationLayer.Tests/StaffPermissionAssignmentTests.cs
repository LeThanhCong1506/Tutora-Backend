using Microsoft.EntityFrameworkCore;
using MV.DomainLayer.Constants;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using MV.InfrastructureLayer.DBContext;
using MV.InfrastructureLayer.Repositories;
using Xunit;

namespace MV.ApplicationLayer.Tests;

public class StaffPermissionAssignmentTests
{
    [Fact]
    public async Task Assignment_AllowsOneGroup_UsesExpectedVersion_AndReflectsLiveRevoke()
    {
        var options = new DbContextOptionsBuilder<AgoraDbContext>()
            .UseInMemoryDatabase($"staff-groups-{Guid.NewGuid()}")
            .Options;
        await using var context = new TestAgoraDbContext(options);

        var now = DateTime.UtcNow;
        var firstGroup = Group("Support", now, Permissions.UserView);
        var secondGroup = Group("Payout", now, Permissions.PayoutView);
        context.PermissionGroups.AddRange(firstGroup, secondGroup);
        await context.SaveChangesAsync();

        var repository = new StaffPermissionRepository(context);
        await repository.SetGroupAssignmentAsync("staff-1", firstGroup.PermissionGroupId, 0, "admin-1", now);
        await context.SaveChangesAsync();

        var first = await repository.GetAssignmentAsync("staff-1");
        Assert.NotNull(first);
        Assert.Equal(firstGroup.PermissionGroupId, first!.PermissionGroupId);
        Assert.Equal(1, first.Version);
        Assert.Equal(new[] { Permissions.UserView }, (await repository.GetGrantedPermissionKeysAsync("staff-1")).ToArray());

        await repository.SetGroupAssignmentAsync("staff-1", secondGroup.PermissionGroupId, 1, "admin-2", now.AddMinutes(1));
        await context.SaveChangesAsync();

        Assert.Single(context.StaffPermissionGroupAssignments);
        var replaced = await repository.GetAssignmentAsync("staff-1");
        Assert.Equal(secondGroup.PermissionGroupId, replaced!.PermissionGroupId);
        Assert.Equal(2, replaced.Version);
        Assert.Equal(new[] { Permissions.PayoutView }, (await repository.GetGrantedPermissionKeysAsync("staff-1")).ToArray());

        var conflict = await Assert.ThrowsAsync<PermissionVersionConflictException>(() =>
            repository.SetGroupAssignmentAsync("staff-1", firstGroup.PermissionGroupId, 1, "admin-3", now));
        Assert.Equal(2, conflict.CurrentVersion);

        await repository.SetGroupAssignmentAsync("staff-1", null, 2, "admin-3", now.AddMinutes(2));
        await context.SaveChangesAsync();

        var revoked = await repository.GetAssignmentAsync("staff-1");
        Assert.Null(revoked!.PermissionGroupId);
        Assert.Equal(3, revoked.Version);
        Assert.Empty(await repository.GetGrantedPermissionKeysAsync("staff-1"));
        Assert.Equal(3, await context.PermissionAuditLogs.CountAsync());
    }

    private static PermissionGroup Group(string name, DateTime now, string permission) => new()
    {
        PermissionGroupId = Guid.NewGuid(),
        Name = name,
        Version = 1,
        CreatedBy = "admin-1",
        CreatedAt = now,
        UpdatedBy = "admin-1",
        UpdatedAt = now,
        Permissions = new List<PermissionGroupPermission>
        {
            new() { PermissionKey = permission }
        }
    };

    private sealed class TestAgoraDbContext(DbContextOptions<AgoraDbContext> options)
        : AgoraDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<QuestionBank>().Ignore(question => question.Embedding);
            modelBuilder.Entity<TutoraKbChunk>().Ignore(chunk => chunk.Embedding);
        }
    }
}
