using Microsoft.EntityFrameworkCore;
using MV.ApplicationLayer.RepositoryInterfaces;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer.DBContext;

namespace MV.InfrastructureLayer.Repositories;

public sealed class PermissionGroupRepository : IPermissionGroupRepository
{
    private readonly AgoraDbContext _context;

    public PermissionGroupRepository(AgoraDbContext context)
    {
        _context = context;
    }

    public async Task<(IReadOnlyList<PermissionGroup> Items, int TotalCount)> GetPagedAsync(
        string? searchTerm, int pageNumber, int pageSize)
    {
        var query = _context.PermissionGroups
            .AsNoTracking()
            .Include(g => g.Permissions)
            .Include(g => g.StaffAssignments)
            .Where(g => !g.IsDeleted);

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim().ToLower();
            query = query.Where(g => g.Name.ToLower().Contains(term)
                || (g.Description != null && g.Description.ToLower().Contains(term)));
        }

        var count = await query.CountAsync();
        var items = await query
            .OrderBy(g => g.Name)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
        return (items, count);
    }

    public Task<PermissionGroup?> GetByIdAsync(Guid id, bool tracked = false)
    {
        IQueryable<PermissionGroup> query = _context.PermissionGroups
            .Include(g => g.Permissions)
            .Include(g => g.StaffAssignments);
        if (!tracked)
            query = query.AsNoTracking();
        return query.FirstOrDefaultAsync(g => g.PermissionGroupId == id && !g.IsDeleted);
    }

    public Task<long?> GetCurrentVersionAsync(Guid id) => _context.PermissionGroups
        .AsNoTracking()
        .Where(g => g.PermissionGroupId == id && !g.IsDeleted)
        .Select(g => (long?)g.Version)
        .FirstOrDefaultAsync();

    public Task<bool> ActiveNameExistsAsync(string normalizedName, Guid? exceptId = null) =>
        _context.PermissionGroups.AnyAsync(g => !g.IsDeleted
            && (!exceptId.HasValue || g.PermissionGroupId != exceptId.Value)
            && g.Name.ToLower() == normalizedName);

    public Task AddAsync(PermissionGroup group) => _context.PermissionGroups.AddAsync(group).AsTask();

    public void ReplacePermissions(PermissionGroup group, IReadOnlyCollection<string> permissionKeys)
    {
        var requested = permissionKeys.ToHashSet(StringComparer.Ordinal);
        var removed = group.Permissions
            .Where(permission => !requested.Contains(permission.PermissionKey))
            .ToList();
        _context.PermissionGroupPermissions.RemoveRange(removed);
        foreach (var permission in removed)
            group.Permissions.Remove(permission);

        var existing = group.Permissions
            .Select(permission => permission.PermissionKey)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var key in requested.Where(key => !existing.Contains(key)))
        {
            group.Permissions.Add(new PermissionGroupPermission
            {
                PermissionGroupId = group.PermissionGroupId,
                PermissionKey = key
            });
        }
    }

    public void AddAudit(PermissionAuditLog auditLog) => _context.PermissionAuditLogs.Add(auditLog);

    public Task<int> CountAssignedStaffAsync(Guid groupId) =>
        _context.StaffPermissionGroupAssignments.CountAsync(a => a.PermissionGroupId == groupId);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);
}
