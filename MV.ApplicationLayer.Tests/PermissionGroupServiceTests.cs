using MV.ApplicationLayer.RepositoryInterfaces;
using MV.ApplicationLayer.Services;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using Xunit;

namespace MV.ApplicationLayer.Tests;

public class PermissionGroupServiceTests
{
    [Fact]
    public async Task CreateUpdateDelete_PreservesVersionsDependenciesAndAudit()
    {
        var repository = new FakePermissionGroupRepository();
        var service = CreateService(repository);

        var created = await service.CreateGroupAsync(new CreatePermissionGroupRequest
        {
            Name = " Vận hành payout ",
            Description = " test ",
            PermissionKeys = new() { Permissions.PayoutView, Permissions.PayoutApprove }
        }, "admin-1");

        Assert.Equal("Vận hành payout", created.Name);
        Assert.Equal(1, created.Version);
        Assert.Equal(2, created.PermissionCount);
        Assert.Single(repository.Audits);

        var updated = await service.UpdateGroupAsync(created.Id, new UpdatePermissionGroupRequest
        {
            Name = created.Name,
            Description = "đã sửa",
            PermissionKeys = new() { Permissions.PayoutView, Permissions.PayoutReject },
            ExpectedVersion = created.Version
        }, "admin-2");

        Assert.Equal(2, updated.Version);
        Assert.Contains(Permissions.PayoutReject, updated.PermissionKeys);
        Assert.DoesNotContain(Permissions.PayoutApprove, updated.PermissionKeys);
        Assert.Equal(2, repository.Audits.Count);

        await service.DeleteGroupAsync(created.Id, updated.Version, "admin-3");
        Assert.True(repository.Groups[created.Id].IsDeleted);
        Assert.Equal(3, repository.Groups[created.Id].Version);
        Assert.Equal(3, repository.Audits.Count);
    }

    [Fact]
    public async Task Update_WithStaleExpectedVersion_ReturnsConflictException()
    {
        var repository = new FakePermissionGroupRepository();
        var service = CreateService(repository);
        var created = await service.CreateGroupAsync(new CreatePermissionGroupRequest
        {
            Name = "Support",
            PermissionKeys = new() { Permissions.UserView }
        }, "admin-1");

        var error = await Assert.ThrowsAsync<PermissionVersionConflictException>(() =>
            service.UpdateGroupAsync(created.Id, new UpdatePermissionGroupRequest
            {
                Name = created.Name,
                PermissionKeys = new() { Permissions.UserView },
                ExpectedVersion = 0
            }, "admin-2"));

        Assert.Equal(1, error.CurrentVersion);
    }

    [Fact]
    public async Task Delete_GroupAssignedToStaff_IsRejected()
    {
        var repository = new FakePermissionGroupRepository { AssignedStaffCount = 1 };
        var service = CreateService(repository);
        var created = await service.CreateGroupAsync(new CreatePermissionGroupRequest
        {
            Name = "Support",
            PermissionKeys = new()
        }, "admin-1");

        var error = await Assert.ThrowsAsync<PermissionGroupInUseException>(() =>
            service.DeleteGroupAsync(created.Id, created.Version, "admin-1"));

        Assert.Equal(1, error.AssignedStaffCount);
        Assert.False(repository.Groups[created.Id].IsDeleted);
    }

    [Fact]
    public async Task Create_ActionWithoutRequiredView_IsRejected()
    {
        var service = CreateService(new FakePermissionGroupRepository());

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateGroupAsync(new CreatePermissionGroupRequest
            {
                Name = "Sai dependency",
                PermissionKeys = new() { Permissions.PayoutApprove }
            }, "admin-1"));

        Assert.Contains(Permissions.PayoutView, error.Message);
    }

    private static PermissionGroupService CreateService(FakePermissionGroupRepository repository) =>
        new(repository, null!, null!, null!);

    private sealed class FakePermissionGroupRepository : IPermissionGroupRepository
    {
        public Dictionary<Guid, PermissionGroup> Groups { get; } = new();
        public List<PermissionAuditLog> Audits { get; } = new();
        public int AssignedStaffCount { get; set; }

        public Task<(IReadOnlyList<PermissionGroup> Items, int TotalCount)> GetPagedAsync(
            string? searchTerm, int pageNumber, int pageSize)
        {
            var groups = Groups.Values.Where(group => !group.IsDeleted).ToList();
            return Task.FromResult(((IReadOnlyList<PermissionGroup>)groups, groups.Count));
        }

        public Task<PermissionGroup?> GetByIdAsync(Guid id, bool tracked = false) =>
            Task.FromResult(Groups.TryGetValue(id, out var group) && !group.IsDeleted ? group : null);

        public Task<long?> GetCurrentVersionAsync(Guid id) =>
            Task.FromResult(Groups.TryGetValue(id, out var group) ? (long?)group.Version : null);

        public Task<bool> ActiveNameExistsAsync(string normalizedName, Guid? exceptId = null) =>
            Task.FromResult(Groups.Values.Any(group => !group.IsDeleted
                && group.PermissionGroupId != exceptId
                && group.Name.Equals(normalizedName, StringComparison.OrdinalIgnoreCase)));

        public Task AddAsync(PermissionGroup group)
        {
            Groups.Add(group.PermissionGroupId, group);
            return Task.CompletedTask;
        }

        public void ReplacePermissions(PermissionGroup group, IReadOnlyCollection<string> permissionKeys)
        {
            group.Permissions = permissionKeys.Select(key => new PermissionGroupPermission
            {
                PermissionGroupId = group.PermissionGroupId,
                PermissionKey = key
            }).ToList();
        }

        public void AddAudit(PermissionAuditLog auditLog) => Audits.Add(auditLog);
        public Task<int> CountAssignedStaffAsync(Guid groupId) => Task.FromResult(AssignedStaffCount);
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
    }
}
