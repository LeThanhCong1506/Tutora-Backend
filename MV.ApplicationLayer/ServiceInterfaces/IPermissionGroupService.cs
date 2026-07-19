using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces;

public interface IPermissionGroupService
{
    Task<PagedList<PermissionGroupSummaryResponse>> GetGroupsAsync(PermissionGroupListParameters parameters);
    Task<PermissionGroupDetailResponse> GetGroupAsync(Guid id);
    Task<PermissionGroupDetailResponse> CreateGroupAsync(CreatePermissionGroupRequest request, string actorUserId);
    Task<PermissionGroupDetailResponse> UpdateGroupAsync(Guid id, UpdatePermissionGroupRequest request, string actorUserId);
    Task DeleteGroupAsync(Guid id, long expectedVersion, string actorUserId);
    Task<StaffPermissionGroupResponse> GetStaffAssignmentAsync(string staffUserId);
    Task<StaffPermissionGroupResponse> SetStaffAssignmentAsync(
        string staffUserId, SetStaffPermissionGroupRequest request, string actorUserId);
    Task<AccessMeResponse> GetAccessAsync(string userId);
}
