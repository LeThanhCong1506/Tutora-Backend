using Microsoft.EntityFrameworkCore;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.RepositoryInterfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using MV.DomainLayer.Helpers;
using System.Text.Json;

namespace MV.ApplicationLayer.Services;

public sealed class PermissionGroupService : IPermissionGroupService
{
    private readonly IPermissionGroupRepository _groups;
    private readonly IStaffPermissionRepository _staffPermissions;
    private readonly IUserRepository _users;
    private readonly IAppDbContext _db;

    public PermissionGroupService(
        IPermissionGroupRepository groups,
        IStaffPermissionRepository staffPermissions,
        IUserRepository users,
        IAppDbContext db)
    {
        _groups = groups;
        _staffPermissions = staffPermissions;
        _users = users;
        _db = db;
    }

    public async Task<PagedList<PermissionGroupSummaryResponse>> GetGroupsAsync(PermissionGroupListParameters parameters)
    {
        var (items, count) = await _groups.GetPagedAsync(
            parameters.SearchTerm, parameters.PageNumber, parameters.PageSize);
        return new PagedList<PermissionGroupSummaryResponse>(
            items.Select(MapSummary).ToList(), count, parameters.PageNumber, parameters.PageSize);
    }

    public async Task<PermissionGroupDetailResponse> GetGroupAsync(Guid id)
    {
        var group = await _groups.GetByIdAsync(id)
            ?? throw new KeyNotFoundException("Không tìm thấy nhóm quyền.");
        return MapDetail(group);
    }

    public async Task<PermissionGroupDetailResponse> CreateGroupAsync(
        CreatePermissionGroupRequest request, string actorUserId)
    {
        var name = NormalizeName(request.Name);
        if (await _groups.ActiveNameExistsAsync(name.ToLowerInvariant()))
            throw new InvalidOperationException("Tên nhóm quyền đã tồn tại.");

        var keys = ValidatePermissionKeys(request.PermissionKeys);
        var now = TimeZoneHelper.UtcNow;
        var group = new PermissionGroup
        {
            PermissionGroupId = Guid.NewGuid(),
            Name = name,
            Description = NormalizeDescription(request.Description),
            Version = 1,
            IsDeleted = false,
            CreatedBy = actorUserId,
            CreatedAt = now,
            UpdatedBy = actorUserId,
            UpdatedAt = now,
            Permissions = keys.Select(key => new PermissionGroupPermission
            {
                PermissionKey = key
            }).ToList()
        };
        await _groups.AddAsync(group);
        AddGroupAudit(group, actorUserId, now, "PERMISSION_GROUP_CREATED", Array.Empty<string>(), keys);
        await _groups.SaveChangesAsync();
        return MapDetail(group);
    }

    public async Task<PermissionGroupDetailResponse> UpdateGroupAsync(
        Guid id, UpdatePermissionGroupRequest request, string actorUserId)
    {
        var group = await _groups.GetByIdAsync(id, tracked: true)
            ?? throw new KeyNotFoundException("Không tìm thấy nhóm quyền.");
        var expectedVersion = request.ExpectedVersion
            ?? throw new ArgumentException("expectedVersion là bắt buộc.");
        EnsureVersion(group.Version, expectedVersion);

        var name = NormalizeName(request.Name);
        if (await _groups.ActiveNameExistsAsync(name.ToLowerInvariant(), id))
            throw new InvalidOperationException("Tên nhóm quyền đã tồn tại.");
        var keys = ValidatePermissionKeys(request.PermissionKeys);
        var previousKeys = group.Permissions.Select(p => p.PermissionKey).OrderBy(k => k).ToArray();
        var now = TimeZoneHelper.UtcNow;

        group.Name = name;
        group.Description = NormalizeDescription(request.Description);
        group.Version++;
        group.UpdatedBy = actorUserId;
        group.UpdatedAt = now;
        _groups.ReplacePermissions(group, keys);
        AddGroupAudit(group, actorUserId, now, "PERMISSION_GROUP_UPDATED", previousKeys, keys);

        await SaveGroupWithConcurrencyAsync(group.PermissionGroupId, expectedVersion);
        return await GetGroupAsync(id);
    }

    public async Task DeleteGroupAsync(Guid id, long expectedVersion, string actorUserId)
    {
        var group = await _groups.GetByIdAsync(id, tracked: true)
            ?? throw new KeyNotFoundException("Không tìm thấy nhóm quyền.");
        EnsureVersion(group.Version, expectedVersion);
        var staffCount = await _groups.CountAssignedStaffAsync(id);
        if (staffCount > 0)
            throw new PermissionGroupInUseException(id, staffCount);

        var now = TimeZoneHelper.UtcNow;
        group.IsDeleted = true;
        group.DeletedAt = now;
        group.UpdatedAt = now;
        group.UpdatedBy = actorUserId;
        group.Version++;
        AddGroupAudit(group, actorUserId, now, "PERMISSION_GROUP_DELETED",
            group.Permissions.Select(p => p.PermissionKey).OrderBy(k => k).ToArray(), Array.Empty<string>());
        await SaveGroupWithConcurrencyAsync(id, expectedVersion);
    }

    public async Task<StaffPermissionGroupResponse> GetStaffAssignmentAsync(string staffUserId)
    {
        var staff = await GetStaffAsync(staffUserId);
        var assignment = await _staffPermissions.GetAssignmentAsync(staffUserId);
        var keys = await _staffPermissions.GetGrantedPermissionKeysAsync(staffUserId);
        return MapStaffAssignment(staff.Userid, staff.Fullname, assignment, keys);
    }

    public async Task<StaffPermissionGroupResponse> SetStaffAssignmentAsync(
        string staffUserId, SetStaffPermissionGroupRequest request, string actorUserId)
    {
        var staff = await GetStaffAsync(staffUserId);
        var expectedVersion = request.ExpectedVersion
            ?? throw new ArgumentException("expectedVersion là bắt buộc.");
        await _staffPermissions.SetGroupAssignmentAsync(
            staffUserId, request.PermissionGroupId, expectedVersion, actorUserId, TimeZoneHelper.UtcNow);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            var current = await _staffPermissions.GetAssignmentAsync(staffUserId);
            throw new PermissionVersionConflictException(
                "Assignment đã thay đổi. Vui lòng tải lại dữ liệu Staff.", current?.Version ?? 0);
        }
        return await GetStaffAssignmentAsync(staff.Userid);
    }

    public async Task<AccessMeResponse> GetAccessAsync(string userId)
    {
        var user = await _users.GetUserByIdAsync(userId)
            ?? throw new KeyNotFoundException("Không tìm thấy người dùng.");
        if (string.Equals(user.Primaryrole, UserRole.Admin, StringComparison.OrdinalIgnoreCase))
        {
            return new AccessMeResponse
            {
                Role = UserRole.Admin,
                PermissionKeys = Permissions.All.OrderBy(k => k).ToList()
            };
        }

        if (!string.Equals(user.Primaryrole, UserRole.Staff, StringComparison.OrdinalIgnoreCase))
            return new AccessMeResponse { Role = user.Primaryrole ?? string.Empty };

        var assignment = await _staffPermissions.GetAssignmentAsync(userId);
        var keys = await _staffPermissions.GetGrantedPermissionKeysAsync(userId);
        return new AccessMeResponse
        {
            Role = UserRole.Staff,
            PermissionKeys = keys.Where(Permissions.All.Contains).OrderBy(k => k).ToList(),
            PermissionGroup = assignment?.PermissionGroup is { IsDeleted: false } group
                ? new PermissionGroupReferenceResponse { Id = group.PermissionGroupId, Name = group.Name }
                : null,
            GroupVersion = assignment?.PermissionGroup is { IsDeleted: false } activeGroup
                ? activeGroup.Version
                : null,
            UpdatedAt = assignment == null
                ? null
                : assignment.PermissionGroup == null || assignment.UpdatedAt >= assignment.PermissionGroup.UpdatedAt
                    ? assignment.UpdatedAt
                    : assignment.PermissionGroup.UpdatedAt
        };
    }

    private static IReadOnlyList<string> ValidatePermissionKeys(IEnumerable<string>? permissionKeys)
    {
        if (permissionKeys == null)
            throw new ArgumentException("permissionKeys là bắt buộc (có thể là mảng rỗng).");
        var keys = permissionKeys
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();
        var unknown = keys.Where(k => !Permissions.All.Contains(k)).ToArray();
        if (unknown.Length > 0)
            throw new ArgumentException($"Permission key không hợp lệ: {string.Join(", ", unknown)}.");

        var selected = keys.ToHashSet(StringComparer.Ordinal);
        var missing = Permissions.Catalog
            .Where(p => selected.Contains(p.Key))
            .SelectMany(p => p.Requires.Select(required => (p.Key, Required: required)))
            .Where(item => !selected.Contains(item.Required))
            .ToArray();
        if (missing.Length > 0)
            throw new ArgumentException("Thiếu permission phụ thuộc: "
                + string.Join(", ", missing.Select(x => $"{x.Key} cần {x.Required}")) + ".");
        return keys;
    }

    private async Task<MV.DomainLayer.Entities.User> GetStaffAsync(string staffUserId)
    {
        var staff = await _users.GetUserByIdAsync(staffUserId)
            ?? throw new KeyNotFoundException("Không tìm thấy Staff.");
        if (!string.Equals(staff.Primaryrole, UserRole.Staff, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"User '{staffUserId}' không phải là Staff.");
        return staff;
    }

    private async Task SaveGroupWithConcurrencyAsync(Guid id, long expectedVersion)
    {
        try
        {
            await _groups.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            var currentVersion = await _groups.GetCurrentVersionAsync(id) ?? expectedVersion;
            throw new PermissionVersionConflictException(
                "Nhóm quyền đã thay đổi. Vui lòng tải lại trước khi lưu.", currentVersion);
        }
    }

    private static void EnsureVersion(long current, long expected)
    {
        if (current != expected)
            throw new PermissionVersionConflictException(
                "Dữ liệu đã thay đổi. Vui lòng tải lại trước khi lưu.", current);
    }

    private static string NormalizeName(string? value)
    {
        var name = value?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Tên nhóm quyền là bắt buộc.");
        if (name.Length > 100)
            throw new ArgumentException("Tên nhóm quyền không được vượt quá 100 ký tự.");
        return name;
    }

    private static string? NormalizeDescription(string? value)
    {
        var description = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (description?.Length > 255)
            throw new ArgumentException("Mô tả không được vượt quá 255 ký tự.");
        return description;
    }

    private static PermissionGroupSummaryResponse MapSummary(PermissionGroup group) => new()
    {
        Id = group.PermissionGroupId,
        Name = group.Name,
        Description = group.Description,
        PermissionCount = group.Permissions.Count,
        TotalPermissionCount = Permissions.All.Count,
        StaffCount = group.StaffAssignments.Count,
        Version = group.Version,
        UpdatedAt = group.UpdatedAt,
        IsActive = !group.IsDeleted
    };

    private static PermissionGroupDetailResponse MapDetail(PermissionGroup group)
    {
        var summary = MapSummary(group);
        return new PermissionGroupDetailResponse
        {
            Id = summary.Id,
            Name = summary.Name,
            Description = summary.Description,
            PermissionCount = summary.PermissionCount,
            TotalPermissionCount = summary.TotalPermissionCount,
            StaffCount = summary.StaffCount,
            Version = summary.Version,
            UpdatedAt = summary.UpdatedAt,
            IsActive = summary.IsActive,
            PermissionKeys = group.Permissions.Select(p => p.PermissionKey).OrderBy(k => k).ToList()
        };
    }

    private static StaffPermissionGroupResponse MapStaffAssignment(
        string staffId,
        string? staffFullName,
        StaffPermissionGroupAssignment? assignment,
        IReadOnlySet<string> permissionKeys) => new()
    {
        StaffId = staffId,
        StaffFullName = staffFullName,
        PermissionGroup = assignment?.PermissionGroup is { IsDeleted: false } group
            ? new PermissionGroupReferenceResponse { Id = group.PermissionGroupId, Name = group.Name }
            : null,
        AssignmentVersion = assignment?.Version ?? 0,
        PermissionKeys = permissionKeys.Where(Permissions.All.Contains).OrderBy(k => k).ToList(),
        UpdatedAt = assignment?.UpdatedAt
    };

    private void AddGroupAudit(PermissionGroup group, string actorUserId, DateTime now,
        string action, IReadOnlyCollection<string> previousKeys, IReadOnlyCollection<string> newKeys)
    {
        _groups.AddAudit(new PermissionAuditLog
        {
            Action = action,
            EntityType = nameof(PermissionGroup),
            EntityId = group.PermissionGroupId.ToString(),
            PermissionGroupId = group.PermissionGroupId,
            Version = group.Version,
            ActorUserId = actorUserId,
            DetailsJson = JsonSerializer.Serialize(new
            {
                group.Name,
                group.Description,
                PreviousPermissionKeys = previousKeys,
                NewPermissionKeys = newKeys
            }),
            CreatedAt = now
        });
    }
}
