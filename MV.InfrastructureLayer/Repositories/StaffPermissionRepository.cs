using Microsoft.EntityFrameworkCore;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer.DBContext;
using MV.ApplicationLayer.RepositoryInterfaces;

namespace MV.InfrastructureLayer.Repositories
{
    public class StaffPermissionRepository : IStaffPermissionRepository
    {
        private readonly AgoraDbContext _context;

        public StaffPermissionRepository(AgoraDbContext context)
        {
            _context = context;
        }

        public async Task<IReadOnlySet<string>> GetGrantedPermissionKeysAsync(string userId)
        {
            var keys = await _context.StaffPermissions
                .AsNoTracking()
                .Where(p => p.Userid == userId)
                .Select(p => p.PermissionKey)
                .ToListAsync();

            return new HashSet<string>(keys, StringComparer.Ordinal);
        }

        public async Task ReplacePermissionsAsync(string userId, IReadOnlyCollection<string> permissionKeys, string grantedBy, DateTime grantedAt)
        {
            var existing = await _context.StaffPermissions
                .Where(p => p.Userid == userId)
                .ToListAsync();

            // Diff instead of blind remove-all + add-all: removing and re-adding a row with the
            // SAME (Userid, PermissionKey) key in one SaveChanges batch makes EF Core's change
            // tracker throw ("another instance with the same key value is already being tracked"),
            // since the old row is still tracked as Deleted when Add() tries to reuse its key.
            var newKeys = new HashSet<string>(permissionKeys, StringComparer.Ordinal);
            var existingKeys = new HashSet<string>(existing.Select(e => e.PermissionKey), StringComparer.Ordinal);

            var toRemove = existing.Where(e => !newKeys.Contains(e.PermissionKey));
            _context.StaffPermissions.RemoveRange(toRemove);

            foreach (var key in newKeys.Where(k => !existingKeys.Contains(k)))
            {
                _context.StaffPermissions.Add(new StaffPermission
                {
                    Userid = userId,
                    PermissionKey = key,
                    GrantedBy = grantedBy,
                    GrantedAt = grantedAt
                });
            }
        }
    }
}
