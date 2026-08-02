using Microsoft.EntityFrameworkCore;
using MV.ApplicationLayer.Services;
using MV.DomainLayer.Constants;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using MV.InfrastructureLayer;
using MV.InfrastructureLayer.DBContext;
using MV.InfrastructureLayer.Repositories;
using Xunit;

namespace MV.ApplicationLayer.Tests;

// Maps to Excel sheet "AdminChangeUserRoleAsync" (Code_42, UserService.AdminChangeUserRoleAsync).
public class AdminChangeUserRoleAsyncTests
{
    private const string AdminId = "admin-1";

    [Fact]
    public async Task RoleNotAssignableByAdmin_ThrowsInvalidOperationException()
    {
        var (service, db) = CreateService();
        db.Users.Add(NewUser("target-1", UserRole.Student));
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AdminChangeUserRoleAsync("target-1", "SuperAdmin", AdminId));
    }

    [Fact]
    public async Task TargetUserNotFound_ThrowsUserNotFoundException()
    {
        var (service, _) = CreateService();

        await Assert.ThrowsAsync<UserNotFoundException>(
            () => service.AdminChangeUserRoleAsync("no-such-user", UserRole.Tutor, AdminId));
    }

    [Fact]
    public async Task AdminChangingOwnRole_ThrowsInvalidOperationException()
    {
        var (service, db) = CreateService();
        db.Users.Add(NewUser(AdminId, UserRole.Admin));
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AdminChangeUserRoleAsync(AdminId, UserRole.Staff, AdminId));
    }

    [Fact]
    public async Task TargetIsAdmin_ThrowsInvalidOperationException()
    {
        var (service, db) = CreateService();
        db.Users.Add(NewUser("other-admin", UserRole.Admin));
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AdminChangeUserRoleAsync("other-admin", UserRole.Staff, AdminId));
    }

    [Fact]
    public async Task ValidChange_UpdatesRoleAndReturnsPreviousAndNewRole()
    {
        var (service, db) = CreateService();
        db.Users.Add(NewUser("target-2", UserRole.Student));
        await db.SaveChangesAsync();

        var result = await service.AdminChangeUserRoleAsync("target-2", UserRole.Tutor, AdminId);

        Assert.Equal(UserRole.Student, result.PreviousRole);
        Assert.Equal(UserRole.Tutor, result.NewRole);
        var updated = await db.Users.AsNoTracking().SingleAsync(u => u.Userid == "target-2");
        Assert.Equal(UserRole.Tutor, updated.Primaryrole);
    }

    [Fact]
    public async Task DemotedOutOfStaff_RevokesPermissionGroupAssignment()
    {
        var (service, db) = CreateService();
        db.Users.Add(NewUser("staff-1", UserRole.Staff));
        db.StaffPermissionGroupAssignments.Add(new StaffPermissionGroupAssignment
        {
            StaffUserId = "staff-1",
            PermissionGroupId = Guid.NewGuid(),
            UpdatedBy = "system",
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        await service.AdminChangeUserRoleAsync("staff-1", UserRole.Parent, AdminId);

        var assignment = await db.StaffPermissionGroupAssignments.AsNoTracking().SingleAsync(a => a.StaffUserId == "staff-1");
        Assert.Null(assignment.PermissionGroupId);
    }

    private static User NewUser(string id, string role) => new()
    {
        Userid = id,
        Password = "hash",
        Fullname = "Người dùng",
        Primaryrole = role,
        Status = 1,
        Createdat = DateTime.UtcNow
    };

    private static (UserService Service, AgoraDbContext Db) CreateService()
    {
        var db = TestSupport.CreateInMemoryContext("admin-change-role");
        var unitOfWork = new UnitOfWork(db, new PasswordRepository(), Microsoft.Extensions.Logging.Abstractions.NullLogger<UnitOfWork>.Instance);
        var service = new UserService(
            unitOfWork,
            new PasswordRepository(),
            null!,
            null!,
            new FakeNotificationService(),
            null!,
            db,
            null!);
        return (service, db);
    }
}
