using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Exceptions;
using MV.DomainLayer.Helpers;
using MV.ApplicationLayer.Interfaces;
using System.Collections.Generic;

namespace MV.ApplicationLayer.Services
{
    public partial class UserService
    {
        // ─── Admin User Management ────────────────────────────────────────────

        public async Task<PagedList<UserResponse>> AdminGetAllUsersAsync(AdminUserFilterParameters parameters)
        {
            var users = await _unitOfWork.UserRepository.GetUsersAsync(parameters);

            // Admin sees every account by default, including Admin and Staff.
            // Optional query parameters below are applied only when explicitly provided.
            var filtered = users.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(parameters.SearchTerm))
            {
                var term = parameters.SearchTerm.ToLower();
                filtered = filtered.Where(u =>
                    (u.Fullname != null && u.Fullname.ToLower().Contains(term)) ||
                    (u.Email != null && u.Email.ToLower().Contains(term)) ||
                    (u.Phone != null && u.Phone.Contains(term)));
            }

            if (!string.IsNullOrWhiteSpace(parameters.Role))
            {
                filtered = filtered.Where(u =>
                    u.Primaryrole != null && u.Primaryrole.Equals(parameters.Role, StringComparison.OrdinalIgnoreCase));
            }

            if (parameters.Status.HasValue)
                filtered = filtered.Where(u => u.Status == parameters.Status.Value);

            if (parameters.CreatedFrom.HasValue)
            {
                var createdFromUtc = parameters.CreatedFrom.Value.Kind == DateTimeKind.Utc
                    ? parameters.CreatedFrom.Value
                    : DateTime.SpecifyKind(parameters.CreatedFrom.Value, DateTimeKind.Utc);
                filtered = filtered.Where(u => u.Createdat >= createdFromUtc);
            }
            if (parameters.CreatedTo.HasValue)
            {
                var createdToUtc = parameters.CreatedTo.Value.Kind == DateTimeKind.Utc
                    ? parameters.CreatedTo.Value
                    : DateTime.SpecifyKind(parameters.CreatedTo.Value, DateTimeKind.Utc);
                filtered = filtered.Where(u => u.Createdat <= createdToUtc);
            }

            var filteredList = filtered.ToList();

            var userResponses = filteredList.Select(u => new UserResponse
            {
                Userid = u.Userid,
                Username = u.Username,
                Email = u.Email,
                Fullname = u.Fullname,
                Phone = u.Phone,
                Address = u.Address,
                Birthdate = u.Birthdate,
                Gender = u.Gender,
                Avatarurl = u.Avatarurl,
                Status = u.Status,
                Createdat = u.Createdat,
                LastLoginAt = u.Lastloginat,
                Role = u.Primaryrole ?? UserRole.User
            }).ToList();

            return new PagedList<UserResponse>(
                userResponses,
                users.TotalCount,
                users.CurrentPage,
                users.PageSize);
        }

        public async Task AdminUpdateUserAsync(string userId, AdminUpdateUserRequest request)
        {
            var user = await _unitOfWork.UserRepository.GetUserByIdAsync(userId)
                ?? throw new UserNotFoundException(userId);

            if (request.Fullname != null) user.Fullname = request.Fullname;
            if (request.Email != null) user.Email = request.Email;
            if (request.Phone != null) user.Phone = request.Phone;

            // Thay đổi role qua general update bị chặn — phải dùng PUT /api/admin/users/{id}/role
            if (request.Primaryrole != null)
                throw new InvalidOperationException("Không thể thay đổi role qua endpoint này. Vui lòng dùng PUT /api/admin/users/{id}/role.");

            if (request.Status.HasValue) user.Status = request.Status.Value;
            if (request.Address != null) user.Address = request.Address;
            if (request.Gender != null) user.Gender = request.Gender;
            if (request.Avatarurl != null) user.Avatarurl = request.Avatarurl;

            await SyncStudentProfileAsync(user);

            await _unitOfWork.UserRepository.UpdateUserAsync(user);
            await _unitOfWork.SaveChangesAsync();
        }

        public async Task<ChangeUserRoleResponse> AdminChangeUserRoleAsync(string targetUserId, string newRole, string adminUserId)
        {
            // Kiểm tra role có nằm trong danh sách cho phép không
            if (!UserRole.AssignableByAdmin.Contains(newRole))
                throw new InvalidOperationException(
                    $"Role '{newRole}' không hợp lệ. Các role được phép gán: {string.Join(", ", UserRole.AssignableByAdmin)}.");

            var targetUser = await _unitOfWork.UserRepository.GetUserByIdAsync(targetUserId)
                ?? throw new UserNotFoundException(targetUserId);

            // Không cho phép Admin tự thay đổi role của chính mình
            if (targetUserId == adminUserId)
                throw new InvalidOperationException("Admin không thể thay đổi role của chính mình.");

            // Không cho phép thay đổi role của tài khoản Admin khác
            if (string.Equals(targetUser.Primaryrole, UserRole.Admin, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Không thể thay đổi role của tài khoản Admin.");

            var previousRole = targetUser.Primaryrole;
            targetUser.Primaryrole = newRole;

            // Rời khỏi role Staff -> xóa hết quyền đã cấp. Nếu không xóa, user này được
            // thăng lại lên Staff sau này sẽ âm thầm có lại toàn bộ quyền cũ mà không qua
            // Admin cấp lại, vi phạm nguyên tắc "Staff mới luôn bắt đầu từ 0 quyền".
            if (string.Equals(previousRole, UserRole.Staff, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(newRole, UserRole.Staff, StringComparison.OrdinalIgnoreCase))
            {
                await _unitOfWork.StaffPermissionRepository.ReplacePermissionsAsync(
                    targetUserId, Array.Empty<string>(), adminUserId, MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow);
            }

            await _unitOfWork.UserRepository.UpdateUserAsync(targetUser);
            await _unitOfWork.SaveChangesAsync();

            return new ChangeUserRoleResponse
            {
                UserId          = targetUser.Userid,
                Fullname        = targetUser.Fullname,
                PreviousRole    = previousRole,
                NewRole         = newRole,
                ChangedByAdminId = adminUserId,
                ChangedAt       = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
            };
        }

        public async Task AdminDeactivateUserAsync(string userId)
        {
            var user = await _unitOfWork.UserRepository.GetUserByIdAsync(userId)
                ?? throw new UserNotFoundException(userId);

            user.Status = 0;
            await _unitOfWork.UserRepository.UpdateUserAsync(user);

            // Also hide tutor profile from search results
            var tutorProfile = await _unitOfWork.UserRepository.GetTutorProfileByIdAsync(userId);
            if (tutorProfile != null)
            {
                tutorProfile.Ispublic = false;
                await _unitOfWork.UserRepository.UpdateTutorProfileAsync(tutorProfile);
            }

            await _unitOfWork.SaveChangesAsync();
        }

        public async Task AdminReactivateUserAsync(string userId)
        {
            var user = await _unitOfWork.UserRepository.GetUserByIdAsync(userId)
                ?? throw new UserNotFoundException(userId);

            user.Status = 1;
            await _unitOfWork.UserRepository.UpdateUserAsync(user);

            // Nếu là gia sư và profile đang Active → khôi phục hiển thị công khai
            var tutorProfile = await _unitOfWork.UserRepository.GetTutorProfileByIdAsync(userId);
            if (tutorProfile != null &&
                string.Equals(tutorProfile.Profilestatus, TutorProfileStatus.Active, StringComparison.OrdinalIgnoreCase))
            {
                tutorProfile.Ispublic = true;
                await _unitOfWork.UserRepository.UpdateTutorProfileAsync(tutorProfile);
            }

            await _unitOfWork.SaveChangesAsync();
        }

        // ─── Admin: xem ảnh CCCD (signed URL, có hiệu lực 10 phút) ──────────────

        public async Task<TutorCccdUrlsResponse> GetTutorCccdUrlsAsync(string tutorId)
        {
            var user = await _unitOfWork.UserRepository.GetUserByIdAsync(tutorId)
                ?? throw new UserNotFoundException(tutorId);

            // URL lưu trong DB là authenticated (private). Phải tạo signed URL mới xem được.
            var frontSigned = !string.IsNullOrEmpty(user.Idcardfronturl)
                ? _storage.GenerateSignedUrl(user.Idcardfronturl)
                : null;

            var backSigned = !string.IsNullOrEmpty(user.Idcardbackurl)
                ? _storage.GenerateSignedUrl(user.Idcardbackurl)
                : null;

            return new TutorCccdUrlsResponse
            {
                TutorId            = user.Userid,
                TutorFullName      = user.Fullname,
                FrontImageUrl      = frontSigned,
                BackImageUrl       = backSigned,
                IsIdentityVerified = user.Isidentityverified ?? false
            };
        }
    }
}
