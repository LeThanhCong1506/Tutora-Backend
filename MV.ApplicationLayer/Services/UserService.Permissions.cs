using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Exceptions;
using MV.DomainLayer.Helpers;

namespace MV.ApplicationLayer.Services
{
    public partial class UserService
    {
        // ─── Admin: phân quyền hành động cho Staff ─────────────────────────────

        public async Task<StaffPermissionsResponse> AdminGetStaffPermissionsAsync(string staffId)
        {
            var staff = await _unitOfWork.UserRepository.GetUserByIdAsync(staffId)
                ?? throw new UserNotFoundException(staffId);

            if (!string.Equals(staff.Primaryrole, UserRole.Staff, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"User '{staffId}' không phải là Staff.");

            var keys = await _unitOfWork.StaffPermissionRepository.GetGrantedPermissionKeysAsync(staffId);

            return new StaffPermissionsResponse
            {
                StaffId = staff.Userid,
                StaffFullName = staff.Fullname,
                PermissionKeys = keys.ToList()
            };
        }

        public async Task<StaffPermissionsResponse> AdminSetStaffPermissionsAsync(string staffId, List<string> permissionKeys, string adminUserId)
        {
            var staff = await _unitOfWork.UserRepository.GetUserByIdAsync(staffId)
                ?? throw new UserNotFoundException(staffId);

            if (!string.Equals(staff.Primaryrole, UserRole.Staff, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"User '{staffId}' không phải là Staff.");

            var unknownKeys = permissionKeys.Where(k => !Permissions.All.Contains(k)).ToList();
            if (unknownKeys.Count > 0)
                throw new InvalidOperationException($"Permission key không hợp lệ: {string.Join(", ", unknownKeys)}.");

            var distinctKeys = permissionKeys.Distinct(StringComparer.Ordinal).ToList();

            await _unitOfWork.StaffPermissionRepository.ReplacePermissionsAsync(
                staffId, distinctKeys, adminUserId, TimeZoneHelper.UtcNow);

            await _unitOfWork.SaveChangesAsync();

            return new StaffPermissionsResponse
            {
                StaffId = staff.Userid,
                StaffFullName = staff.Fullname,
                PermissionKeys = distinctKeys
            };
        }
    }
}
