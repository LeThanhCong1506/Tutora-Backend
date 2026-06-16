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
        private readonly IUnitOfWork _unitOfWork;
        private readonly IPasswordRepository _passwordRepository;
        private readonly ITutorVerificationService _verificationService;
        private readonly IFileStorageService _storage;
        private readonly INotificationService _notificationService;
        private const string UserAvatarBucket = StorageBucket.Avatars;

        public UserService(
            IUnitOfWork unitOfWork,
            IPasswordRepository passwordRepository,
            ITutorVerificationService verificationService,
            IFileStorageService storage,
            INotificationService notificationService)
        {
            _unitOfWork = unitOfWork;
            _passwordRepository = passwordRepository;
            _verificationService = verificationService;
            _storage = storage;
            _notificationService = notificationService;
        }

        // ─── Queries ──────────────────────────────────────────────────────────

        public async Task<UserResponse> GetUserByIdAsync(string userId)
        {
            var user = await _unitOfWork.UserRepository.GetUserByIdAsync(userId)
                ?? throw new UserNotFoundException();

            return new UserResponse
            {
                Userid = user.Userid,
                Username = user.Username,
                Email = user.Email,
                Fullname = user.Fullname,
                Phone = user.Phone,
                Isidentityverified = user.Isidentityverified,
                Idcardfronturl = user.Idcardfronturl,
                Idcardbackurl = user.Idcardbackurl,
                Birthdate = user.Birthdate,
                Address = user.Address,
                Gender = user.Gender,
                Avatarurl = user.Avatarurl,
                Status = user.Status,
                Createdat = user.Createdat.HasValue ? TimeZoneHelper.ToUserTime(user.Createdat.Value) : (DateTime?)null,
                LastLoginAt = user.Lastloginat.HasValue ? TimeZoneHelper.ToUserTime(user.Lastloginat.Value) : (DateTime?)null,
                Role = user.Primaryrole,
                Ekycrawdata = user.Ekycrawdata
            };
        }

        public async Task<PagedList<UserResponse>> GetUsersByRoleAsync(string roleName, UserParameters parameters)
        {
            var users = await _unitOfWork.UserRepository.GetUsersByRoleAsync(roleName, parameters);
            var mapped = users.Select(MapToUserResponse).ToList();
            return new PagedList<UserResponse>(mapped, users.TotalCount, users.CurrentPage, users.PageSize);
        }

        public async Task<PagedList<UserResponse>> GetTutorsBySubjectAsync(int subjectId, UserParameters parameters)
        {
            var tutors = await _unitOfWork.UserRepository.GetTutorsBySubjectAsync(subjectId, parameters);
            var mapped = tutors.Select(MapToUserResponse).ToList();
            return new PagedList<UserResponse>(mapped, tutors.TotalCount, tutors.CurrentPage, tutors.PageSize);
        }

        public async Task<PagedList<PendingTutorResponse>> GetPendingTutorsAsync(UserParameters parameters)
        {
            var users = await _unitOfWork.UserRepository.GetPendingTutorsAsync(parameters);

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
                    Createdat = u.Createdat.HasValue ? TimeZoneHelper.ToUserTime(u.Createdat.Value) : (DateTime?)null,
                    ProfileStatus = u.Tutorprofile?.Profilestatus,
                    RejectionNote = u.Tutorprofile?.Rejectionnote,
                    ProfileCreatedAt = u.Tutorprofile?.Createdat.HasValue == true ? TimeZoneHelper.ToUserTime(u.Tutorprofile.Createdat.Value) : (DateTime?)null,
                    ProfileUpdatedAt = u.Tutorprofile?.Updatedat.HasValue == true ? TimeZoneHelper.ToUserTime(u.Tutorprofile.Updatedat.Value) : (DateTime?)null,
                    Sections = progress?.Sections ?? new VerificationSections()
                });
            }

            return new PagedList<PendingTutorResponse>(mappedUsers, users.TotalCount, users.CurrentPage, users.PageSize);
        }

        public async Task<TutorProfileShortResponse> GetTutorProfileShortAsync(string tutorId)
        {
            var profile = await _unitOfWork.UserRepository.GetTutorProfileByIdAsync(tutorId)
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
            var studentProfiles = await _unitOfWork.UserRepository.GetStudentProfilesByParentIdAsync(parentId);

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

        public async Task<UserResponse> CreateUserAsync(CreateUserRequest request)
        {
            if (!await _unitOfWork.UserRepository.IsEmailUniqueAsync(request.Email))
                throw new EmailAlreadyExistsException();
            if (!await _unitOfWork.UserRepository.IsUsernameUniqueAsync(request.Username))
                throw new UsernameAlreadyExistsException();
            if (!await _unitOfWork.UserRepository.IsIdentityNumberUniqueAsync(request.IdentityNumber))
                throw new IdentityNumberAlreadyExistsException();
            if (!string.IsNullOrEmpty(request.Phone) && !await _unitOfWork.UserRepository.IsPhoneUniqueAsync(request.Phone))
                throw new PhoneAlreadyExistsException();

            var roleToUse = !string.IsNullOrEmpty(request.RoleName) ? request.RoleName : UserRole.Parent;
            var userId = Guid.NewGuid().ToString();

            var newUser = new User
            {
                Userid = userId,
                Username = request.Username,
                Email = request.Email,
                Password = _passwordRepository.HashPassword(request.Password),
                Fullname = request.Fullname,
                Identitynumber = request.IdentityNumber,
                Phone = request.Phone,
                Birthdate = request.Birthdate,
                Address = request.Address,
                Gender = request.Gender,
                Status = 1,
                Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow,
                Primaryrole = roleToUse
            };

            if (string.Equals(roleToUse, UserRole.Tutor, StringComparison.OrdinalIgnoreCase))
            {
                newUser.Tutorprofile = new Tutorprofile
                {
                    Tutorid = userId,
                    Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow,
                    Profilestatus = TutorProfileStatus.Draft
                };
            }
            else if (string.Equals(roleToUse, UserRole.Student, StringComparison.OrdinalIgnoreCase))
            {
                newUser.StudentprofileLinkedusers.Add(new Studentprofile
                {
                    Studentid = $"STU-{userId}",
                    Linkeduserid = userId,
                    Parentid = request.ParentId,
                    Fullname = request.Fullname,
                    Birthdate = request.Birthdate,
                    Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
                });
            }
            else if (string.Equals(roleToUse, UserRole.Parent, StringComparison.OrdinalIgnoreCase))
            {
                newUser.Wallet = new Wallet { Userid = userId, Balance = 0, Lastupdated = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow };
            }

            await _unitOfWork.UserRepository.CreateUserAsync(newUser);
            await _unitOfWork.SaveChangesAsync();

            return await GetUserByIdAsync(newUser.Userid);
        }

        public async Task CreateTutorSubjectAsync(string tutorId, SelectTutorSubjectRequest request)
        {
            if (!await _unitOfWork.UserRepository.SubjectExistsAsync(request.SubjectId))
                throw new KeyNotFoundException("Môn học này không tồn tại trong hệ thống.");

            if (await _unitOfWork.UserRepository.HasTutorAlreadySelectedSubjectAsync(tutorId, request.SubjectId))
                throw new InvalidOperationException("Bạn đã đăng ký môn học này rồi.");

            var tutorSubject = new Tutorsubject
            {
                Tutorid = tutorId,
                Subjectid = request.SubjectId,
                Gradelevels = JsonSerializer.Serialize(request.GradeLevels),
                Tags = request.Tags
            };

            await _unitOfWork.UserRepository.CreateTutorSubjectAsync(tutorSubject);
            await _unitOfWork.SaveChangesAsync();
        }

        public async Task UpdateUserAsync(string userId, UpdateUserRequest request)
        {
            var user = await _unitOfWork.UserRepository.GetUserByIdAsync(userId)
                ?? throw new UserNotFoundException();

            user.Fullname = request.Fullname;
            user.Address = request.Address;
            user.Avatarurl = request.Avatarurl;
            user.Birthdate = request.Birthdate;
            user.Gender = request.Gender;

            await _unitOfWork.UserRepository.UpdateUserAsync(user);
            await _unitOfWork.SaveChangesAsync();
        }

        public async Task DeleteUserAsync(string userId)
        {
            var user = await _unitOfWork.UserRepository.GetUserByIdAsync(userId)
                ?? throw new UserNotFoundException();

            await _unitOfWork.RefreshTokenRepository.RevokeAllByUserIdAsync(userId);
            await _unitOfWork.UserRepository.DeleteUserAsync(user);
            await _unitOfWork.SaveChangesAsync();
        }

        // ─── Self-deactivation ────────────────────────────────────────────────

        public async Task<DeactivationStatusResponse> ToggleDeactivationAsync(string userId)
        {
            var user = await _unitOfWork.UserRepository.GetUserByIdAsync(userId)
                ?? throw new UserNotFoundException();

            var now = TimeZoneHelper.UtcNow;
            var willDeactivate = !(user.Isdeactivated ?? false);

            user.Isdeactivated = willDeactivate;
            user.Deactivatedat = now;

            // Nếu là Tutor: ẩn/hiện hồ sơ tương ứng
            if (user.Tutorprofile != null)
                user.Tutorprofile.Ispublic = !willDeactivate;

            await _unitOfWork.UserRepository.UpdateUserAsync(user);
            await _unitOfWork.SaveChangesAsync();

            var message = willDeactivate
                ? "Tài khoản của bạn đã được tạm khóa thành công. Bạn có thể mở lại bất cứ lúc nào bằng cách đăng nhập."
                : "Tài khoản của bạn đã được mở lại thành công.";

            return new DeactivationStatusResponse
            {
                IsDeactivated = willDeactivate,
                DeactivatedAt = TimeZoneHelper.ToUserTime(now),
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

            var user = await _unitOfWork.UserRepository.GetUserByIdAsync(userId)
                ?? throw new UserNotFoundException(userId);

            var avatarUrl = await _storage.UploadFileAsync(UserAvatarBucket, userId, avatarFile);
            user.Avatarurl = avatarUrl;
            await _unitOfWork.UserRepository.UpdateUserAsync(user);
            await _unitOfWork.SaveChangesAsync();

            return avatarUrl;
        }

        // ─── Private helpers ──────────────────────────────────────────────────

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
            Createdat = user.Createdat.HasValue ? TimeZoneHelper.ToUserTime(user.Createdat.Value) : (DateTime?)null,
            LastLoginAt = user.Lastloginat.HasValue ? TimeZoneHelper.ToUserTime(user.Lastloginat.Value) : (DateTime?)null,
            Role = user.Primaryrole,
            Ekycrawdata = user.Ekycrawdata,
            Isidentityverified = user.Isidentityverified,
            Idcardfronturl = user.Idcardfronturl,
            Idcardbackurl = user.Idcardbackurl
        };
    }
}
