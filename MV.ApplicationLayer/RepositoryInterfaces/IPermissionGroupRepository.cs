using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.RepositoryInterfaces;

public interface IPermissionGroupRepository
{
    Task<(IReadOnlyList<PermissionGroup> Items, int TotalCount)> GetPagedAsync(
        string? searchTerm, int pageNumber, int pageSize);
    Task<PermissionGroup?> GetByIdAsync(Guid id, bool tracked = false);
    Task<long?> GetCurrentVersionAsync(Guid id);
    Task<bool> ActiveNameExistsAsync(string normalizedName, Guid? exceptId = null);
    Task AddAsync(PermissionGroup group);
    void ReplacePermissions(PermissionGroup group, IReadOnlyCollection<string> permissionKeys);
    void AddAudit(PermissionAuditLog auditLog);
    Task<int> CountAssignedStaffAsync(Guid groupId);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
