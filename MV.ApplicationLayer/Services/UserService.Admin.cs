using Microsoft.EntityFrameworkCore;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.DTO.ResponseModel.Admin;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using MV.DomainLayer.Helpers;
using MV.ApplicationLayer.Interfaces;
using System.Collections.Generic;

namespace MV.ApplicationLayer.Services
{
    public partial class UserService
    {
        // ─── Admin User Management ────────────────────────────────────────────

        public async Task<PagedList<UserResponse>> AdminGetAllUsersAsync(AdminUserFilterParameters parameters, bool includeInternalAccounts = true)
        {
            // Build the filtered query at the DB level so pagination AND the
            // returned TotalCount stay correct. Previously the repository paged
            // the entire users table first and these filters ran in-memory on
            // that single page — which made TotalCount reflect all users (not the
            // filtered set) and silently dropped matching rows the moment a role,
            // status or search term was applied. That broke every filtered/paged
            // view (and would break the new role-scoped views entirely).
            var query = _context.Users.AsNoTracking().AsQueryable();

            // Admin sees every account, including internal (Admin/Staff).
            // Delegated staff only ever see customer accounts.
            if (!includeInternalAccounts)
                query = query.Where(u =>
                    u.Primaryrole != UserRole.Admin && u.Primaryrole != UserRole.Staff);

            if (!string.IsNullOrWhiteSpace(parameters.SearchTerm))
            {
                var term = parameters.SearchTerm.Trim().ToLower();
                query = query.Where(u =>
                    (u.Fullname != null && u.Fullname.ToLower().Contains(term)) ||
                    (u.Email != null && u.Email.ToLower().Contains(term)) ||
                    (u.Phone != null && u.Phone.Contains(term)));
            }

            if (!string.IsNullOrWhiteSpace(parameters.Role))
            {
                var role = parameters.Role.ToLower();
                query = query.Where(u => u.Primaryrole != null && u.Primaryrole.ToLower() == role);
            }

            if (parameters.Status.HasValue)
                query = query.Where(u => u.Status == parameters.Status.Value);

            if (parameters.CreatedFrom.HasValue)
            {
                var createdFromUtc = parameters.CreatedFrom.Value.Kind == DateTimeKind.Utc
                    ? parameters.CreatedFrom.Value
                    : DateTime.SpecifyKind(parameters.CreatedFrom.Value, DateTimeKind.Utc);
                query = query.Where(u => u.Createdat >= createdFromUtc);
            }
            if (parameters.CreatedTo.HasValue)
            {
                var createdToUtc = parameters.CreatedTo.Value.Kind == DateTimeKind.Utc
                    ? parameters.CreatedTo.Value
                    : DateTime.SpecifyKind(parameters.CreatedTo.Value, DateTimeKind.Utc);
                query = query.Where(u => u.Createdat <= createdToUtc);
            }

            query = parameters.OrderBy switch
            {
                UserListSortBy.FullName => query.OrderBy(u => u.Fullname),
                UserListSortBy.FullNameDesc => query.OrderByDescending(u => u.Fullname),
                UserListSortBy.Email => query.OrderBy(u => u.Email),
                UserListSortBy.CreatedAt => query.OrderBy(u => u.Createdat),
                _ => query.OrderByDescending(u => u.Createdat) // Default: newest first
            };

            var pageNumber = parameters.PageNumber < 1 ? 1 : parameters.PageNumber;
            var pageSize = parameters.PageSize; // already clamped to <= MaxPageSize by the setter

            var totalCount = await query.CountAsync();
            var pageUsers = await query
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // Warning / suspension counts for just this page, as two grouped
            // queries rather than one per user.
            var pageUserIds = pageUsers.Select(u => u.Userid).ToList();

            var warningCounts = await _context.Userwarnings
                .AsNoTracking()
                .Where(w => w.Userid != null && pageUserIds.Contains(w.Userid))
                .GroupBy(w => w.Userid!)
                .Select(g => new { UserId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.UserId, x => x.Count);

            var suspensionCounts = await _context.Profilesuspensions
                .AsNoTracking()
                .Where(s => s.Userid != null && pageUserIds.Contains(s.Userid))
                .GroupBy(s => s.Userid!)
                .Select(g => new { UserId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.UserId, x => x.Count);

            var userResponses = pageUsers.Select(u => new UserResponse
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
                Role = u.Primaryrole ?? UserRole.User,
                WarningsCount = warningCounts.GetValueOrDefault(u.Userid),
                SuspensionsCount = suspensionCounts.GetValueOrDefault(u.Userid)
            }).ToList();

            return new PagedList<UserResponse>(
                userResponses,
                totalCount,
                pageNumber,
                pageSize);
        }

        public async Task<AdminUserDetailResponse> AdminGetUserDetailAsync(string userId)
        {
            var user = await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(candidate => candidate.Userid == userId)
                ?? throw new UserNotFoundException();

            var relationships = new AdminUserRelationshipsResponse();

            if (string.Equals(user.Primaryrole, UserRole.Parent, StringComparison.OrdinalIgnoreCase))
            {
                relationships.Students = await (
                    from profile in _context.Studentprofiles.AsNoTracking()
                    join linkedUser in _context.Users.AsNoTracking()
                        on profile.Linkeduserid equals linkedUser.Userid into linkedUsers
                    from linkedUser in linkedUsers.DefaultIfEmpty()
                    where profile.Parentid == userId
                    orderby profile.Fullname
                    select new AdminLinkedUserResponse
                    {
                        UserId = linkedUser == null ? null : linkedUser.Userid,
                        FullName = linkedUser != null && !string.IsNullOrEmpty(linkedUser.Fullname)
                            ? linkedUser.Fullname
                            : profile.Fullname ?? "Học sinh chưa đặt tên",
                        Email = linkedUser == null ? null : linkedUser.Email,
                        Phone = linkedUser == null ? null : linkedUser.Phone,
                        AvatarUrl = linkedUser != null && linkedUser.Avatarurl != null
                            ? linkedUser.Avatarurl
                            : profile.Avatarurl,
                        Role = UserRole.Student,
                        StudentProfileId = profile.Studentid,
                        HasAccount = linkedUser != null
                    })
                    .ToListAsync();
            }
            else if (string.Equals(user.Primaryrole, UserRole.Student, StringComparison.OrdinalIgnoreCase))
            {
                relationships.Parent = await (
                    from profile in _context.Studentprofiles.AsNoTracking()
                    join parent in _context.Users.AsNoTracking()
                        on profile.Parentid equals parent.Userid
                    where profile.Linkeduserid == userId || profile.Studentid == userId
                    orderby profile.Createdat, profile.Studentid
                    select new AdminLinkedUserResponse
                    {
                        UserId = parent.Userid,
                        FullName = parent.Fullname ?? parent.Username ?? "Phụ huynh chưa đặt tên",
                        Email = parent.Email,
                        Phone = parent.Phone,
                        AvatarUrl = parent.Avatarurl,
                        Role = UserRole.Parent,
                        StudentProfileId = profile.Studentid,
                        HasAccount = true
                    })
                    .FirstOrDefaultAsync();
            }

            return new AdminUserDetailResponse
            {
                User = MapToUserResponse(user),
                Relationships = relationships
            };
        }

        public async Task<UserResponse> AdminCreateUserAsync(AdminCreateUserRequest request, string adminUserId)
        {
            // Only customer roles are created here. Internal accounts (Admin/Staff)
            // have their own onboarding flow (POST /api/staffs) and must not be
            // reachable through this endpoint.
            var canonicalRole =
                string.Equals(request.Role, UserRole.Student, StringComparison.OrdinalIgnoreCase) ? UserRole.Student :
                string.Equals(request.Role, UserRole.Parent, StringComparison.OrdinalIgnoreCase) ? UserRole.Parent :
                string.Equals(request.Role, UserRole.Tutor, StringComparison.OrdinalIgnoreCase) ? UserRole.Tutor :
                throw new InvalidOperationException("Vai trò không hợp lệ. Chỉ được tạo tài khoản Student, Parent hoặc Tutor.");

            var phone = request.Phone.Trim();
            if (!await _unitOfWork.UserRepository.IsPhoneUniqueAsync(phone))
                throw new PhoneAlreadyExistsException();

            var email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
            if (email != null && !await _unitOfWork.UserRepository.IsEmailUniqueAsync(email))
                throw new EmailAlreadyExistsException();

            var now = TimeZoneHelper.UtcNow;
            var userId = Guid.NewGuid().ToString();
            var fullname = request.Fullname.Trim();

            var newUser = new User
            {
                Userid = userId,
                Email = email,
                Phone = phone,
                Password = _passwordRepository.HashPassword(request.Password),
                Fullname = fullname,
                Status = 1,
                Isemailverified = false,
                // Admin-created → vouched. Skip the OTP gate so the account can
                // sign in immediately (customer login requires a verified phone).
                Isphoneverified = true,
                Createdat = now,
                Primaryrole = canonicalRole
            };

            // Each customer role owns a side-entity, mirroring self-registration.
            if (canonicalRole == UserRole.Tutor)
            {
                newUser.Tutorprofile = new Tutorprofile
                {
                    Tutorid = userId,
                    Createdat = now,
                    Profilestatus = TutorProfileStatus.Draft
                };
            }
            else if (canonicalRole == UserRole.Student)
            {
                newUser.StudentprofileLinkedusers.Add(new Studentprofile
                {
                    Studentid = userId,
                    Parentid = null,
                    Fullname = fullname,
                    Createdat = now
                });
            }
            else if (canonicalRole == UserRole.Parent)
            {
                newUser.Wallet = new Wallet
                {
                    Userid = userId,
                    Balance = 0,
                    Lastupdated = now
                };
            }

            await _unitOfWork.UserRepository.CreateUserAsync(newUser);
            await _unitOfWork.SaveChangesAsync();

            return await GetUserByIdAsync(userId);
        }

        public async Task AdminUpdateUserAsync(string userId, AdminUpdateUserRequest request)
        {
            var user = await _unitOfWork.UserRepository.GetUserByIdAsync(userId)
                ?? throw new UserNotFoundException(userId);

            if (request.Fullname != null) user.Fullname = request.Fullname;
            if (request.Email != null) user.Email = request.Email;
            if (request.Phone != null) user.Phone = request.Phone;


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
                await _unitOfWork.StaffPermissionRepository.RevokeGroupAssignmentAsync(
                    targetUserId, adminUserId, MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow);
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
