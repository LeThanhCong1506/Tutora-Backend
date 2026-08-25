using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using MV.DomainLayer.Helpers;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.RepositoryInterfaces;
using System.Text.Json;

namespace MV.ApplicationLayer.Services
{
    public partial class UserService : IUserService
    {
        private readonly IUserRepository _userRepository;
        private readonly ITutorRepository _tutorRepository;
        private readonly IStudentRepository _studentRepository;
        private readonly IRefreshTokenRepository _refreshTokenRepository;
        private readonly IStaffPermissionRepository _staffPermissionRepository;
        private readonly IPasswordRepository _passwordRepository;
        private readonly ITutorVerificationService _verificationService;
        private readonly IFileStorageService _storage;
        private readonly INotificationService _notificationService;
        private readonly IEncryptionService _encryption;
        private readonly IAppDbContext _context;
        private readonly ITutorEmbedQueue _embedQueue;
        private readonly ISuspensionRefundService _suspensionRefundService;
        private readonly ILogger<UserService> _logger;
        private const string UserAvatarBucket = StorageBucket.Avatars;

        public UserService(
            IUserRepository userRepository,
            ITutorRepository tutorRepository,
            IStudentRepository studentRepository,
            IRefreshTokenRepository refreshTokenRepository,
            IStaffPermissionRepository staffPermissionRepository,
            IPasswordRepository passwordRepository,
            ITutorVerificationService verificationService,
            IFileStorageService storage,
            INotificationService notificationService,
            IEncryptionService encryption,
            IAppDbContext context,
            ITutorEmbedQueue embedQueue,
            ISuspensionRefundService suspensionRefundService,
            ILogger<UserService> logger)
        {
            _userRepository = userRepository;
            _tutorRepository = tutorRepository;
            _studentRepository = studentRepository;
            _refreshTokenRepository = refreshTokenRepository;
            _staffPermissionRepository = staffPermissionRepository;
            _passwordRepository = passwordRepository;
            _verificationService = verificationService;
            _storage = storage;
            _notificationService = notificationService;
            _encryption = encryption;
            _context = context;
            _embedQueue = embedQueue;
            _suspensionRefundService = suspensionRefundService;
            _logger = logger;
        }

        // ─── Queries ──────────────────────────────────────────────────────────

        public async Task<UserResponse> GetUserByIdAsync(string userId)
        {
            var user = await _userRepository.GetUserByIdAsync(userId)
                ?? throw new UserNotFoundException();

            return new UserResponse
            {
                Userid = user.Userid,
                Username = user.Username,
                Email = user.Email,
                Fullname = user.Fullname,
                Phone = user.Phone,
                Isidentityverified = user.Isidentityverified,
                Birthdate = user.Birthdate,
                Address = user.Address,
                Gender = user.Gender,
                Avatarurl = user.Avatarurl,
                Status = user.Status,
                Createdat = user.Createdat,
                LastLoginAt = user.Lastloginat,
                Role = user.Primaryrole
            };
        }

        public async Task<PagedList<UserResponse>> GetUsersByRoleAsync(string roleName, UserParameters parameters)
        {
            var users = await _userRepository.GetUsersByRoleAsync(roleName, parameters);
            var mapped = users.Select(MapToUserResponse).ToList();
            if (string.Equals(roleName, UserRole.Staff, StringComparison.OrdinalIgnoreCase))
            {
                var assignments = await _staffPermissionRepository.GetAssignmentsAsync(
                    mapped.Select(user => user.Userid).ToArray());
                foreach (var response in mapped)
                {
                    if (!assignments.TryGetValue(response.Userid, out var assignment))
                        continue;
                    response.AssignmentVersion = assignment.Version;
                    if (assignment.PermissionGroup is { IsDeleted: false } group)
                    {
                        response.PermissionGroup = new PermissionGroupReferenceResponse
                        {
                            Id = group.PermissionGroupId,
                            Name = group.Name
                        };
                    }
                }
            }
            return new PagedList<UserResponse>(mapped, users.TotalCount, users.CurrentPage, users.PageSize);
        }

        public async Task<PagedList<UserResponse>> GetTutorsBySubjectAsync(int subjectId, UserParameters parameters)
        {
            var tutors = await _userRepository.GetTutorsBySubjectAsync(subjectId, parameters);
            var mapped = tutors.Select(MapToUserResponse).ToList();
            return new PagedList<UserResponse>(mapped, tutors.TotalCount, tutors.CurrentPage, tutors.PageSize);
        }

        public async Task<PagedList<PendingTutorResponse>> GetPendingTutorsAsync(UserParameters parameters)
        {
            var users = await _userRepository.GetPendingTutorsAsync(parameters);

            var mappedUsers = new List<PendingTutorResponse>();
            foreach (var u in users)
            {
                var progress = await _verificationService.GetVerificationProgressAsync(u.Userid);
                mappedUsers.Add(new PendingTutorResponse
                {
                    Userid = u.Userid,
                    Username = u.Username,
                    Fullname = u.Fullname,
                    Email = u.Email,
                    Phone = u.Phone,
                    Avatarurl = u.Avatarurl,
                    Birthdate = u.Birthdate,
                    Gender = u.Gender,
                    Address = u.Address,
                    Status = u.Status,
                    Createdat = u.Createdat,
                    ProfileStatus = u.Tutorprofile?.Profilestatus,
                    ProfileCreatedAt = u.Tutorprofile?.Createdat.HasValue == true ? u.Tutorprofile.Createdat.Value : (DateTime?)null,
                    ProfileUpdatedAt = u.Tutorprofile?.Updatedat.HasValue == true ? u.Tutorprofile.Updatedat.Value : (DateTime?)null,
                    Sections = progress?.Sections ?? new VerificationSections()
                });
            }

            return new PagedList<PendingTutorResponse>(mappedUsers, users.TotalCount, users.CurrentPage, users.PageSize);
        }

        public async Task<TutorProfileShortResponse> GetTutorProfileShortAsync(string tutorId)
        {
            var profile = await _userRepository.GetTutorProfileByIdAsync(tutorId)
                ?? throw new KeyNotFoundException("Không tìm thấy hồ sơ gia sư của người dùng này.");

            return new TutorProfileShortResponse
            {
                TutorId = profile.Tutorid,
                Headline = profile.Headline,
                Bio = profile.Bio
            };
        }

        public async Task<List<StudentProfileResponse>> GetStudentsByParentIdAsync(string parentId)
        {
            var studentProfiles = await _userRepository.GetStudentProfilesByParentIdAsync(parentId);

            return studentProfiles.Select(sp => new StudentProfileResponse
            {
                StudentId = sp.Studentid,
                FullName = sp.Fullname,
                BirthDate = sp.Birthdate,
                School = sp.School,
                GradeLevel = sp.Gradelevel
            }).ToList();
        }

        // ─── CRUD ─────────────────────────────────────────────────────────────

        // Endpoint tạo user duy nhất do Admin gọi là tạo NHÂN VIÊN (Staff).
        // Tài khoản khách hàng (Tutor/Parent/Student) đăng ký qua auth flows
        // (SimpleAuth/Social) — không đi qua đây, nên không còn branch theo role.
        // Chỉ nhận trường tối thiểu; hồ sơ cá nhân staff tự bổ sung sau qua
        // UpdateUserAsync (PUT /api/users/{id}).
        public async Task<UserResponse> CreateStaffAsync(CreateStaffRequest request, string adminUserId)
        {
            if (!await _userRepository.IsEmailUniqueAsync(request.Email))
                throw new EmailAlreadyExistsException();
            if (!string.IsNullOrEmpty(request.Username) && !await _userRepository.IsUsernameUniqueAsync(request.Username))
                throw new UsernameAlreadyExistsException();
            if (!string.IsNullOrEmpty(request.Phone) && !await _userRepository.IsPhoneUniqueAsync(request.Phone))
                throw new PhoneAlreadyExistsException();

            PermissionGroup? group = null;
            if (request.PermissionGroupId.HasValue)
            {
                group = await _context.PermissionGroups
                    .AsNoTracking()
                    .FirstOrDefaultAsync(g => g.PermissionGroupId == request.PermissionGroupId.Value && !g.IsDeleted)
                    ?? throw new KeyNotFoundException("Không tìm thấy nhóm quyền đang hoạt động.");
            }

            var now = TimeZoneHelper.UtcNow;
            var userId = Guid.NewGuid().ToString();
            var newUser = new User
            {
                Userid = userId,
                Username = request.Username,
                Email = request.Email,
                Password = _passwordRepository.HashPassword(request.Password),
                Fullname = request.Fullname,
                Phone = request.Phone,
                Status = 1,
                Createdat = now,
                Primaryrole = UserRole.Staff
            };

            await _userRepository.CreateUserAsync(newUser);
            var assignment = new StaffPermissionGroupAssignment
            {
                StaffUserId = userId,
                PermissionGroupId = group?.PermissionGroupId,
                Version = group == null ? 0 : 1,
                UpdatedBy = adminUserId,
                UpdatedAt = now,
                StaffUser = newUser
            };
            _context.StaffPermissionGroupAssignments.Add(assignment);
            _context.PermissionAuditLogs.Add(new PermissionAuditLog
            {
                Action = group == null ? "STAFF_CREATED_WITHOUT_GROUP" : "STAFF_GROUP_ASSIGNED",
                EntityType = nameof(StaffPermissionGroupAssignment),
                EntityId = userId,
                PermissionGroupId = group?.PermissionGroupId,
                StaffUserId = userId,
                Version = assignment.Version,
                ActorUserId = adminUserId,
                DetailsJson = JsonSerializer.Serialize(new
                {
                    PreviousGroupId = (Guid?)null,
                    NewGroupId = group?.PermissionGroupId
                }),
                CreatedAt = now
            });
            await _context.SaveChangesAsync();

            var response = await GetUserByIdAsync(newUser.Userid);
            response.AssignmentVersion = assignment.Version;
            if (group != null)
            {
                response.PermissionGroup = new PermissionGroupReferenceResponse
                {
                    Id = group.PermissionGroupId,
                    Name = group.Name
                };
            }
            return response;
        }
        public async Task CreateTutorSubjectAsync(string tutorId, SelectTutorSubjectRequest request)
        {
            if (!await _userRepository.SubjectExistsAsync(request.SubjectId))
                throw new KeyNotFoundException("Môn học này không tồn tại trong hệ thống.");

            if (await _userRepository.HasTutorAlreadySelectedSubjectAsync(tutorId, request.SubjectId))
                throw new InvalidOperationException("Bạn đã đăng ký môn học này rồi.");

            var tutorSubject = new Tutorsubject
            {
                Tutorid = tutorId,
                Subjectid = request.SubjectId,
                Gradelevels = JsonSerializer.Serialize(request.GradeLevels),
                Tags = request.Tags
            };

            await _userRepository.CreateTutorSubjectAsync(tutorSubject);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateUserAsync(string userId, UpdateUserRequest request)
        {
            var user = await _userRepository.GetUserByIdAsync(userId)
                ?? throw new UserNotFoundException();

            var studentProfile = await _studentRepository.FindByStudentOrLinkedUserAsync(userId);

            // Đã xác minh CCCD (Student hoặc Tutor qua eKYC): họ tên & ngày sinh lấy từ CCCD là nguồn
            // chuẩn, không cho tự sửa (cùng quy tắc với StudentService.UpdateSelfProfileAsync).
            // Isidentityverified chỉ được set true bởi EkycService/StudentIdentityService nên không
            // cần giới hạn theo role.
            var identityLocked = user.Isidentityverified == true;

            if (!identityLocked)
                user.Fullname = request.Fullname;
            user.Address = request.Address;
            user.Avatarurl = request.Avatarurl;
            if (!identityLocked)
                user.Birthdate = request.Birthdate;
            user.Gender = request.Gender;

            if (!string.IsNullOrWhiteSpace(request.Email))
            {
                var email = request.Email.Trim();
                if (!string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
                {
                    if (!await _userRepository.IsEmailUniqueAsync(email))
                        throw new EmailAlreadyExistsException();
                    user.Email = email;
                }
            }

            SyncStudentProfile(user, studentProfile);

            await _userRepository.UpdateUserAsync(user);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteUserAsync(string userId)
        {
            var user = await _userRepository.GetUserByIdAsync(userId)
                ?? throw new UserNotFoundException();

            await _refreshTokenRepository.RevokeAllByUserIdAsync(userId);
            await _userRepository.DeleteUserAsync(user);
            await _context.SaveChangesAsync();
        }

        // ─── Self-deactivation ────────────────────────────────────────────────

        public async Task<DeactivationStatusResponse> ToggleDeactivationAsync(string userId)
        {
            var user = await _userRepository.GetUserByIdAsync(userId)
                ?? throw new UserNotFoundException();

            var now = TimeZoneHelper.UtcNow;
            var willDeactivate = !(user.Isdeactivated ?? false);

            user.Isdeactivated = willDeactivate;
            user.Deactivatedat = now;

            // Nếu là Tutor: ẩn/hiện hồ sơ tương ứng
            if (user.Tutorprofile != null)
                user.Tutorprofile.Ispublic = !willDeactivate;

            await _userRepository.UpdateUserAsync(user);
            await _context.SaveChangesAsync();

            var message = willDeactivate
                ? "Tài khoản của bạn đã được tạm khóa thành công. Bạn có thể mở lại bất cứ lúc nào bằng cách đăng nhập."
                : "Tài khoản của bạn đã được mở lại thành công.";

            return new DeactivationStatusResponse
            {
                IsDeactivated = willDeactivate,
                DeactivatedAt = now,
                Message = message
            };
        }

        // ─── Avatar ───────────────────────────────────────────────────────────

        public async Task<string?> UpdateUserAvatarAsync(string userId, IFormFile avatarFile)
        {
            if (avatarFile == null || avatarFile.Length == 0)
                throw new ArgumentException("Vui lòng chọn ảnh đại diện");

            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            var extension = Path.GetExtension(avatarFile.FileName).ToLowerInvariant();
            if (!allowedExtensions.Contains(extension))
                throw new ArgumentException("Chỉ chấp nhận các định dạng JPG, PNG và WebP");
            if (avatarFile.Length > 5 * 1024 * 1024)
                throw new ArgumentException("Ảnh đại diện phải nhỏ hơn 5MB");

            var user = await _userRepository.GetUserByIdAsync(userId)
                ?? throw new UserNotFoundException(userId);

            var avatarUrl = await _storage.UploadFileAsync(UserAvatarBucket, userId, avatarFile);
            user.Avatarurl = avatarUrl;
            await SyncStudentProfileAsync(user);
            await _userRepository.UpdateUserAsync(user);
            await _context.SaveChangesAsync();

            return avatarUrl;
        }

        // ─── Private helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Studentprofile giữ bản sao Fullname/Birthdate/Avatarurl của học sinh — lịch học, booking,
        /// settlement, export... đều đọc từ đó. Mọi chỗ ghi 3 trường này trên bảng Users phải gọi
        /// hàm này để 2 bảng không bị lệch. Không làm gì nếu user không phải học sinh.
        /// </summary>
        private async Task SyncStudentProfileAsync(User user)
            => SyncStudentProfile(user, await _studentRepository.FindByStudentOrLinkedUserAsync(user.Userid));

        private static void SyncStudentProfile(User user, Studentprofile? profile)
        {
            if (profile == null) return;

            profile.Fullname = user.Fullname;
            profile.Birthdate = user.Birthdate;
            profile.Avatarurl = user.Avatarurl;
        }

        private static UserResponse MapToUserResponse(User user) => new UserResponse
        {
            Userid = user.Userid,
            Username = user.Username,
            Email = user.Email,
            Fullname = user.Fullname,
            Phone = user.Phone,
            Birthdate = user.Birthdate,
            Address = user.Address,
            Gender = user.Gender,
            Avatarurl = user.Avatarurl,
            Status = user.Status,
            Createdat = user.Createdat,
            LastLoginAt = user.Lastloginat,
            Role = user.Primaryrole,
            Isidentityverified = user.Isidentityverified
        };
    }
}
