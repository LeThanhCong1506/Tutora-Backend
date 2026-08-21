using Microsoft.EntityFrameworkCore;
using MV.ApplicationLayer.Helpers;
using MV.DomainLayer.Constants;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer.DBContext;
using Xunit;

namespace MV.ApplicationLayer.Tests;

/// <summary>
/// Người nhận thông báo nghiệp vụ trong CMS = Admin (superuser bypass) + Staff được gán nhóm quyền
/// tương ứng. Trước đây mọi luồng chỉ query Admin nên Staff không bao giờ biết có việc mới.
/// </summary>
public class PermissionRecipientsTests
{
    [Fact]
    public async Task ResolveAsync_ReturnsAdminsPlusStaffHoldingThatPermission()
    {
        await using var context = CreateContext();
        context.Users.AddRange(
            CreateUser("admin-1", UserRole.Admin),
            CreateUser("staff-payout", UserRole.Staff),
            CreateUser("staff-support", UserRole.Staff),
            CreateUser("tutor-1", UserRole.Tutor));
        AddGroupWithStaff(context, Permissions.PayoutView, "staff-payout");
        AddGroupWithStaff(context, Permissions.SupportView, "staff-support");
        await context.SaveChangesAsync();

        var payoutReviewers = await PermissionRecipients.ResolveAsync(context, Permissions.PayoutView);
        var supportReviewers = await PermissionRecipients.ResolveAsync(context, Permissions.SupportView);

        Assert.Equal(
            new[] { "admin-1", "staff-payout" },
            payoutReviewers.OrderBy(id => id, StringComparer.Ordinal));
        Assert.Equal(
            new[] { "admin-1", "staff-support" },
            supportReviewers.OrderBy(id => id, StringComparer.Ordinal));
    }

    /// <summary>Một nhóm quyền có thể chứa nhiều key — staff trong đó nhận thông báo của cả hai nghiệp vụ.</summary>
    [Fact]
    public async Task ResolveAsync_MatchesAnyKeyInsideTheGroup()
    {
        await using var context = CreateContext();
        context.Users.Add(CreateUser("staff-multi", UserRole.Staff));
        AddGroupWithStaff(context, Permissions.PayoutView, "staff-multi", Permissions.DisputeView);
        await context.SaveChangesAsync();

        Assert.Contains("staff-multi", await PermissionRecipients.ResolveAsync(context, Permissions.PayoutView));
        Assert.Contains("staff-multi", await PermissionRecipients.ResolveAsync(context, Permissions.DisputeView));
        Assert.Empty(await PermissionRecipients.ResolveAsync(context, Permissions.SupportView));
    }

    /// <summary>
    /// SupportMessageService dựa vào tham số này để Admin vừa trả lời không tự nhận thông báo
    /// về chính tin nhắn mình gửi.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_ExcludesTheActorWhoTriggeredTheEvent()
    {
        await using var context = CreateContext();
        context.Users.AddRange(
            CreateUser("admin-1", UserRole.Admin),
            CreateUser("admin-2", UserRole.Admin));
        await context.SaveChangesAsync();

        var recipients = await PermissionRecipients.ResolveAsync(
            context, Permissions.SupportView, excludeUserId: "admin-1");

        Assert.Equal(new[] { "admin-2" }, recipients);
    }

    /// <summary>Đổi role khỏi Staff thì bản ghi gán nhóm quyền còn đó, nhưng không được báo nữa.</summary>
    [Fact]
    public async Task ResolveAsync_IgnoresAssignmentWhoseUserIsNoLongerStaff()
    {
        await using var context = CreateContext();
        context.Users.Add(CreateUser("ex-staff", UserRole.Parent));
        AddGroupWithStaff(context, Permissions.PayoutView, "ex-staff");
        await context.SaveChangesAsync();

        Assert.Empty(await PermissionRecipients.ResolveAsync(context, Permissions.PayoutView));
    }

    private static AgoraDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AgoraDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new RecipientsTestDbContext(options);
    }

    private static User CreateUser(string id, string role) => new()
    {
        Userid = id,
        Username = id,
        Password = "test-hash",
        Email = $"{id}@test.local",
        Fullname = id,
        Primaryrole = role,
        Status = 1,
        Createdat = DateTime.UtcNow
    };

    private static void AddGroupWithStaff(
        AgoraDbContext context, string permissionKey, string staffUserId, params string[] extraKeys)
    {
        var groupId = Guid.NewGuid();
        var keys = new[] { permissionKey }.Concat(extraKeys);
        context.PermissionGroups.Add(new PermissionGroup
        {
            PermissionGroupId = groupId,
            Name = $"group-{staffUserId}",
            CreatedBy = "admin-1",
            CreatedAt = DateTime.UtcNow,
            UpdatedBy = "admin-1",
            UpdatedAt = DateTime.UtcNow,
            Permissions = keys
                .Select(key => new PermissionGroupPermission { PermissionGroupId = groupId, PermissionKey = key })
                .ToList()
        });
        context.StaffPermissionGroupAssignments.Add(new StaffPermissionGroupAssignment
        {
            StaffUserId = staffUserId,
            PermissionGroupId = groupId,
            UpdatedBy = "admin-1",
            UpdatedAt = DateTime.UtcNow
        });
    }

    private sealed class RecipientsTestDbContext(DbContextOptions<AgoraDbContext> options)
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
