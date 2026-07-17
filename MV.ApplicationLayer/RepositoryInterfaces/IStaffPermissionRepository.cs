using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.RepositoryInterfaces
{
    public interface IStaffPermissionRepository
    {
        Task<IReadOnlySet<string>> GetGrantedPermissionKeysAsync(string userId);

        Task<StaffPermissionGroupAssignment?> GetAssignmentAsync(string staffUserId, bool tracked = false);

        Task<IReadOnlyDictionary<string, StaffPermissionGroupAssignment>> GetAssignmentsAsync(
            IReadOnlyCollection<string> staffUserIds);

        Task SetGroupAssignmentAsync(
            string staffUserId,
            Guid? permissionGroupId,
            long expectedVersion,
            string updatedBy,
            DateTime updatedAt);

        Task RevokeGroupAssignmentAsync(string staffUserId, string updatedBy, DateTime updatedAt);
    }
}
