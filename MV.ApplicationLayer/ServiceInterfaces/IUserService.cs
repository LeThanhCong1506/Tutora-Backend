using Microsoft.AspNetCore.Http;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces
{
    public interface IUserService
    {
        /// <summary>
        /// Single user by id.
        /// </summary>
        Task<UserResponse> GetUserByIdAsync(string userId);

        /// <summary>
        /// Paged list of users filtered by a specific role name.
        /// </summary>
        Task<PagedList<UserResponse>> GetUsersByRoleAsync(string roleName, UserParameters parameters);

        /// <summary>
        /// Paged list of tutors who teach a given subject.
        /// </summary>
        Task<PagedList<UserResponse>> GetTutorsBySubjectAsync(int subjectId, UserParameters parameters);

        /// <summary>
        /// Paged list of tutors whose profile is pending admin approval.
        /// </summary>
        Task<PagedList<PendingTutorResponse>> GetPendingTutorsAsync(UserParameters parameters);

        /// <summary>
        /// 4-field tutor embed (id, name, avatar, headline) for booking/lesson display.
        /// </summary>
        Task<TutorProfileShortResponse> GetTutorProfileShortAsync(string tutorId);

        /// <summary>
        /// Admin approves or rejects a tutor's profile submission.
        /// </summary>
        Task<ApproveTutorResponse> ApproveTutorProfileAsync(string tutorId, ApproveTutorRequest request, string adminId);

        /// <summary>
        /// All student profiles belonging to a parent account.
        /// </summary>
        Task<List<StudentProfileResponse>> GetStudentsByParentIdAsync(string parentId);

        /// <summary>
        /// Create a new user account (admin-initiated).
        /// </summary>
        Task<UserResponse> CreateUserAsync(CreateUserRequest request);

        /// <summary>
        /// Assign subjects a tutor is qualified to teach.
        /// </summary>
        Task CreateTutorSubjectAsync(string tutorId, SelectTutorSubjectRequest request);

        /// <summary>
        /// Update basic user fields (display name, phone, etc.).
        /// </summary>
        Task UpdateUserAsync(string userId, UpdateUserRequest request);

        /// <summary>
        /// Tutor updates their own profile (bio, education, experience, pricing).
        /// </summary>
        Task UpdateTutorProfileAsync(string userId, UpdateTutorProfileRequest request);

        /// <summary>
        /// Replace a tutor's entire weekly availability schedule in one call.
        /// </summary>
        Task UpdateTutorWeeklyAvailabilityAsync(string tutorId, UpdateTutorScheduleRequest request);

        /// <summary>
        /// Hard-delete a user account (admin only).
        /// </summary>
        Task DeleteUserAsync(string userId);

        /// <summary>
        /// Re-evaluate and update a tutor's profile status based on completeness rules.
        /// </summary>
        Task AutoUpdateTutorProfileStatusAsync(string tutorId);

        // ── Admin user management ──────────────────────────────────────────

        /// <summary>
        /// Admin: paged user list with extended filter parameters.
        /// </summary>
        Task<PagedList<UserResponse>> AdminGetAllUsersAsync(AdminUserFilterParameters parameters);

        /// <summary>
        /// Admin: update any user's fields (including role assignment).
        /// </summary>
        Task AdminUpdateUserAsync(string userId, AdminUpdateUserRequest request);

        /// <summary>
        /// Admin: soft-deactivate a user account (status = 0).
        /// </summary>
        Task AdminDeactivateUserAsync(string userId);

        /// <summary>
        /// Admin: reactivate a previously deactivated user account (status = 1).
        /// Nếu là gia sư và profile đang Active → khôi phục Ispublic = true.
        /// </summary>
        Task AdminReactivateUserAsync(string userId);

        /// <summary>
        /// Admin: change a user's role. Only roles in <see cref="UserRole.AssignableByAdmin"/> are permitted.
        /// The "Admin" role cannot be assigned via API.
        /// </summary>
        Task<ChangeUserRoleResponse> AdminChangeUserRoleAsync(string targetUserId, string newRole, string adminUserId);

        /// <summary>
        /// Upload and replace a user's avatar image; returns the new public URL.
        /// </summary>
        Task<string?> UpdateUserAvatarAsync(string userId, IFormFile avatarFile);

        /// <summary>
        /// Toggle self-deactivation: khóa tài khoản nếu đang mở, mở tài khoản nếu đang khóa.
        /// Lưu thời điểm thay đổi trạng thái vào <c>Deactivatedat</c>.
        /// </summary>
        Task<DeactivationStatusResponse> ToggleDeactivationAsync(string userId);

        /// <summary>
        /// Admin only: lấy signed URL có thời hạn 15 phút để xem ảnh CCCD của gia sư.
        /// URL lưu trong DB là private/authenticated — không thể truy cập trực tiếp.
        /// </summary>
        Task<TutorCccdUrlsResponse> GetTutorCccdUrlsAsync(string tutorId);
    }
}
