using Microsoft.EntityFrameworkCore;
using MV.ApplicationLayer.RepositoryInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using MV.InfrastructureLayer.DBContext;
using System.Text.Json;

namespace MV.InfrastructureLayer.Repositories;

public class StaffPermissionRepository : IStaffPermissionRepository
{
    private readonly AgoraDbContext _context;

    public StaffPermissionRepository(AgoraDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlySet<string>> GetGrantedPermissionKeysAsync(string userId)
    {
        // Legacy staff_permissions rows remain rollback-only after migration.
        var keys = await _context.StaffPermissionGroupAssignments
            .AsNoTracking()
            .Where(a => a.StaffUserId == userId
                && a.PermissionGroupId != null
                && a.PermissionGroup != null
                && !a.PermissionGroup.IsDeleted)
            .SelectMany(a => a.PermissionGroup!.Permissions.Select(p => p.PermissionKey))
            .ToListAsync();

        return new HashSet<string>(keys.Where(Permissions.All.Contains), StringComparer.Ordinal);
    }

    public Task<StaffPermissionGroupAssignment?> GetAssignmentAsync(string staffUserId, bool tracked = false)
    {
        IQueryable<StaffPermissionGroupAssignment> query = _context.StaffPermissionGroupAssignments
            .Include(a => a.PermissionGroup);
        if (!tracked)
            query = query.AsNoTracking();
        return query.FirstOrDefaultAsync(a => a.StaffUserId == staffUserId);
    }

    public async Task<IReadOnlyDictionary<string, StaffPermissionGroupAssignment>> GetAssignmentsAsync(
        IReadOnlyCollection<string> staffUserIds)
    {
        if (staffUserIds.Count == 0)
            return new Dictionary<string, StaffPermissionGroupAssignment>(StringComparer.Ordinal);

        var assignments = await _context.StaffPermissionGroupAssignments
            .AsNoTracking()
            .Include(a => a.PermissionGroup)
            .Where(a => staffUserIds.Contains(a.StaffUserId))
            .ToListAsync();
        return assignments.ToDictionary(a => a.StaffUserId, StringComparer.Ordinal);
    }

    public async Task SetGroupAssignmentAsync(
        string staffUserId,
        Guid? permissionGroupId,
        long expectedVersion,
        string updatedBy,
        DateTime updatedAt)
    {
        if (permissionGroupId.HasValue)
        {
            var groupExists = await _context.PermissionGroups
                .AnyAsync(g => g.PermissionGroupId == permissionGroupId.Value && !g.IsDeleted);
            if (!groupExists)
                throw new KeyNotFoundException("Không tìm thấy nhóm quyền đang hoạt động.");
        }

        var assignment = await _context.StaffPermissionGroupAssignments
            .FirstOrDefaultAsync(a => a.StaffUserId == staffUserId);
        var previousGroupId = assignment?.PermissionGroupId;

        if (assignment == null)
        {
            if (expectedVersion != 0)
                throw new PermissionVersionConflictException(
                    "Assignment đã thay đổi. Vui lòng tải lại dữ liệu Staff.", 0);
            assignment = new StaffPermissionGroupAssignment
            {
                StaffUserId = staffUserId,
                PermissionGroupId = permissionGroupId,
                Version = 1,
                UpdatedBy = updatedBy,
                UpdatedAt = updatedAt
            };
            _context.StaffPermissionGroupAssignments.Add(assignment);
        }
        else
        {
            if (assignment.Version != expectedVersion)
                throw new PermissionVersionConflictException(
                    "Assignment đã thay đổi. Vui lòng tải lại dữ liệu Staff.", assignment.Version);
            assignment.PermissionGroupId = permissionGroupId;
            assignment.Version++;
            assignment.UpdatedBy = updatedBy;
            assignment.UpdatedAt = updatedAt;
        }

        AddAssignmentAudit(
            assignment,
            previousGroupId,
            permissionGroupId,
            updatedBy,
            updatedAt,
            permissionGroupId.HasValue ? "STAFF_GROUP_ASSIGNED" : "STAFF_GROUP_UNASSIGNED");
    }

    public async Task RevokeGroupAssignmentAsync(string staffUserId, string updatedBy, DateTime updatedAt)
    {
        var assignment = await _context.StaffPermissionGroupAssignments
            .FirstOrDefaultAsync(a => a.StaffUserId == staffUserId);
        if (assignment == null || assignment.PermissionGroupId == null)
            return;

        var previousGroupId = assignment.PermissionGroupId;
        assignment.PermissionGroupId = null;
        assignment.Version++;
        assignment.UpdatedBy = updatedBy;
        assignment.UpdatedAt = updatedAt;
        AddAssignmentAudit(assignment, previousGroupId, null, updatedBy, updatedAt, "STAFF_GROUP_REVOKED");
    }

    private void AddAssignmentAudit(
        StaffPermissionGroupAssignment assignment,
        Guid? previousGroupId,
        Guid? newGroupId,
        string actorUserId,
        DateTime changedAt,
        string action)
    {
        _context.PermissionAuditLogs.Add(new PermissionAuditLog
        {
            Action = action,
            EntityType = nameof(StaffPermissionGroupAssignment),
            EntityId = assignment.StaffUserId,
            PermissionGroupId = newGroupId,
            StaffUserId = assignment.StaffUserId,
            Version = assignment.Version,
            ActorUserId = actorUserId,
            DetailsJson = JsonSerializer.Serialize(new
            {
                PreviousGroupId = previousGroupId,
                NewGroupId = newGroupId
            }),
            CreatedAt = changedAt
        });
    }
}
