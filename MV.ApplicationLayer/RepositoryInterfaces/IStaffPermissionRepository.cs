namespace MV.ApplicationLayer.RepositoryInterfaces
{
    public interface IStaffPermissionRepository
    {
        Task<IReadOnlySet<string>> GetGrantedPermissionKeysAsync(string userId);
        Task ReplacePermissionsAsync(string userId, IReadOnlyCollection<string> permissionKeys, string grantedBy, DateTime grantedAt);
    }
}
