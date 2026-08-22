using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using MV.DomainLayer.Helpers;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.RepositoryInterfaces;
using System.Security.Cryptography;

namespace MV.ApplicationLayer.Services
{
    public partial class StudentService : IStudentService
    {
        private readonly IUserRepository _userRepository;
        private readonly IStudentRepository _studentRepository;
        private readonly IAppDbContext _dbContext;
        private readonly IFileStorageService _storage;
        private readonly IConfiguration _config;
        private readonly IPasswordRepository _passwordRepository;
        private readonly IStudentIdentityService _identity;
        private readonly IWalletRepository _walletRepository;
        private readonly IAiCreditService _aiCreditService;
        private readonly Microsoft.Extensions.Logging.ILogger<StudentService> _logger;
        private const int MaxStudentsPerParent = 5;
        private const string AvatarBucket = StorageBucket.Avatars;
        private const int UsernameMaxRetries = 100;
        private const string DefaultUsernameBase = "student";
        private const int GeneratedPasswordLength = 10;
        private const string PasswordChars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789!@#$";

        public StudentService(
            IUserRepository userRepository,
            IStudentRepository studentRepository,
            IAppDbContext dbContext,
            IFileStorageService storage,
            IConfiguration config,
            IPasswordRepository passwordRepository,
            IStudentIdentityService identity,
            IWalletRepository walletRepository,
            IAiCreditService aiCreditService,
            Microsoft.Extensions.Logging.ILogger<StudentService> logger)
        {
            _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
            _studentRepository = studentRepository ?? throw new ArgumentNullException(nameof(studentRepository));
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _passwordRepository = passwordRepository ?? throw new ArgumentNullException(nameof(passwordRepository));
            _identity = identity ?? throw new ArgumentNullException(nameof(identity));
            _walletRepository = walletRepository ?? throw new ArgumentNullException(nameof(walletRepository));
            _aiCreditService = aiCreditService ?? throw new ArgumentNullException(nameof(aiCreditService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<List<StudentProfileResponse>> GetStudentsByParentIdAsync(string parentId)
        {
            var students = await _studentRepository.GetByParentIdAsync(parentId);
            var responses = new List<StudentProfileResponse>();
            foreach (var s in students)
            {
                var resp = MapToResponse(s);
                if (s.Linkeduserid != null)
                {
                    var user = await _userRepository.GetUserByIdAsync(s.Linkeduserid);
                    resp.Username = user?.Username;
                }
                responses.Add(resp);
            }
            return responses;
        }

        public async Task<StudentProfileResponse> GetStudentByIdAsync(string studentId, string parentId)
        {
            var student = await _studentRepository.GetByIdAndParentAsync(studentId, parentId)
                ?? throw new StudentNotFoundException(studentId);
            var resp = MapToResponse(student);
            if (student.Linkeduserid != null)
            {
                var user = await _userRepository.GetUserByIdAsync(student.Linkeduserid);
                resp.Username = user?.Username;
            }
            return resp;
        }

        public async Task<StudentCredentialsResponse> CreateStudentAsync(CreateStudentRequest request, string parentId)
        {
            ValidateBirthdate(request.Birthdate);

            var currentCount = await _studentRepository.CountByParentIdAsync(parentId);
            if (currentCount >= MaxStudentsPerParent)
                throw new MaxStudentsReachedException();

            // SĐT phụ huynh.
            var parent = await _userRepository.GetUserByIdAsync(parentId);

            // 1. Auto-generate credentials cho child
            var childUserId = Guid.NewGuid().ToString();
            var username = await GenerateUniqueUsernameAsync(request.Fullname);
            var tempPassword = GenerateSecurePassword();

            // 2. Tạo User account cho child (role = Student)
            var childUser = new User
            {
                Userid = childUserId,
                Username = username,
                Password = _passwordRepository.HashPassword(tempPassword),
                Email = $"{childUserId}@no-email.tutora.production.com",
                Fullname = request.Fullname,
                Birthdate = request.Birthdate,
                Status = 1,
                Isemailverified = true, // tài khoản do parent tạo, dùng email giả — không cần verify
                Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow,
                Primaryrole = UserRole.Student
            };

            // 3. Tạo Studentprofile
            var student = new Studentprofile
            {
                Studentid = await GenerateStudentIdAsync(),
                Parentid = parentId,
                Linkeduserid = childUserId,
                Fullname = request.Fullname,
                Birthdate = request.Birthdate,
                School = request.School,
                Gradelevelid = request.GradeLevelId,
                Learninggoals = request.Learninggoals,
                Parentphone = parent?.Phone,
                Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
            };

            // 4. Lưu cả User và Studentprofile
            await _userRepository.CreateUserAsync(childUser);
            await _studentRepository.CreateAsync(student);
            await _dbContext.SaveChangesAsync();

            // 4b. Tặng gói Free cho TÀI KHOẢN của học sinh (credit gắn với user_id).
            try
            {
                await _aiCreditService.GrantFreePackageAsync(childUserId);
            }
            catch (Exception ex)
            {
                Microsoft.Extensions.Logging.LoggerExtensions.LogError(
                    _logger, ex, "Failed to grant Free AI credit to new student {StudentId}", student.Studentid);
            }

            // 5. Trả credentials cho parent (password chỉ hiển thị 1 lần)
            return new StudentCredentialsResponse
            {
                StudentId = student.Studentid,
                UserId = childUserId,
                Username = username,
                TemporaryPassword = tempPassword,
                FullName = request.Fullname,
                ParentId = parentId,
                CreatedAt = student.Createdat ?? MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
            };
        }

        public async Task<StudentProfileResponse> UpdateAvatarAsync(string studentId, IFormFile avatarFile, string parentId)
        {
            var student = await _studentRepository.GetByIdAndParentAsync(studentId, parentId)
                ?? throw new NotStudentOwnerException(studentId);

            var oldFilePath = student.Avatarurl;
            if (oldFilePath != null)
                await _storage.DeleteFileAsync(AvatarBucket, studentId, oldFilePath);

            student.Avatarurl = await UploadAvatarAsync(avatarFile, parentId);
            _studentRepository.Update(student);

            if (student.Linkeduserid != null)
            {
                var linkedUser = await _userRepository.GetUserByIdAsync(student.Linkeduserid);
                if (linkedUser != null)
                {
                    linkedUser.Avatarurl = student.Avatarurl;
                    await _userRepository.UpdateUserAsync(linkedUser);
                }
            }

            await _dbContext.SaveChangesAsync();

            return MapToResponse(student);
        }

        public async Task<StudentProfileResponse> UpdateStudentAsync(string studentId, UpdateStudentRequest request, string parentId)
        {
            ValidateBirthdate(request.Birthdate);

            var student = await _studentRepository.GetByIdAndParentAsync(studentId, parentId)
                ?? throw new NotStudentOwnerException(studentId);

            student.Fullname = request.Fullname;
            student.Birthdate = request.Birthdate;
            student.School = request.School;
            student.Gradelevelid = request.GradeLevelId;
            student.Learninggoals = request.Learninggoals;

            _studentRepository.Update(student);

            // Cũng cập nhật User.Fullname để đồng bộ tên ở mọi nơi
            if (student.Linkeduserid != null)
            {
                var linkedUser = await _userRepository.GetUserByIdAsync(student.Linkeduserid);
                if (linkedUser != null)
                {
                    linkedUser.Fullname = request.Fullname;
                    linkedUser.Birthdate = request.Birthdate;
                    await _userRepository.UpdateUserAsync(linkedUser);
                }
            }

            await _dbContext.SaveChangesAsync();

            return MapToResponse(student);
        }

        public async Task DeleteStudentAsync(string studentId, string parentId)
        {
            var student = await _studentRepository.GetByIdAndParentAsync(studentId, parentId)
                ?? throw new NotStudentOwnerException(studentId);

            if (await _studentRepository.HasActiveBookingAsync(studentId))
                throw new StudentHasActiveBookingException();

            _studentRepository.SoftDelete(student);
            await _dbContext.SaveChangesAsync();
        }


        /// <summary>
        /// Reset password cho student (chỉ parent sở hữu mới được gọi)
        /// </summary>
        public async Task<StudentCredentialsResponse> ResetStudentPasswordAsync(string studentId, string parentId)
        {
            var student = await _studentRepository.GetByIdAndParentAsync(studentId, parentId)
                ?? throw new NotStudentOwnerException(studentId);

            if (string.IsNullOrEmpty(student.Linkeduserid))
                throw new InvalidOperationException("Học sinh chưa có tài khoản liên kết.");

            var user = await _userRepository.GetUserByIdAsync(student.Linkeduserid)
                ?? throw new InvalidOperationException("Không tìm thấy tài khoản liên kết.");

            // Generate new password
            var newPassword = GenerateSecurePassword();
            user.Password = _passwordRepository.HashPassword(newPassword);

            await _userRepository.UpdateUserAsync(user);
            await _dbContext.SaveChangesAsync();

            return new StudentCredentialsResponse
            {
                StudentId = student.Studentid,
                UserId = user.Userid,
                Username = user.Username ?? "",
                TemporaryPassword = newPassword,
                FullName = student.Fullname ?? "",
                ParentId = parentId,
                CreatedAt = student.Createdat ?? MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
            };
        }

        public async Task<StudentLinkStatusResponse> GetLinkStatusAsync(string studentUserId)
        {
            var profiles = await _studentRepository.GetByLinkedUserIdAsync(studentUserId);
            var student = profiles.FirstOrDefault(p => p.Parentid != null) ?? profiles.FirstOrDefault();

            if (student == null)
            {
                return new StudentLinkStatusResponse { Linked = false };
            }

            var linked = student.Parentid != null;

            string? parentName = null;
            if (linked)
            {
                var parent = await _userRepository.GetUserByIdAsync(student.Parentid!);
                parentName = parent?.Fullname;
            }

            // Trạng thái xác minh độ tuổi nằm ở bảng Users.
            var linkedUser = await _userRepository.GetUserByIdAsync(studentUserId);
            var profileResp = MapToResponse(student);
            profileResp.IsIdentityVerified = linkedUser?.Isidentityverified == true;

            return new StudentLinkStatusResponse
            {
                Linked = linked,
                ParentName = parentName,
                ParentId = student.Parentid,
                StudentProfile = profileResp
            };
        }

        public async Task<StudentProfileResponse> UpdateSelfAvatarAsync(string studentUserId, IFormFile avatarFile)
        {
            var student = await _studentRepository.FindByStudentOrLinkedUserAsync(studentUserId)
                ?? throw new StudentNotFoundException();

            var oldFilePath = student.Avatarurl;
            if (oldFilePath != null)
                await _storage.DeleteFileAsync(AvatarBucket, studentUserId, oldFilePath);

            var newUrl = await _storage.UploadFileAsync(AvatarBucket, studentUserId, avatarFile);
            student.Avatarurl = newUrl;
            _studentRepository.Update(student);

            // Cập nhật User để 2 bảng luôn đồng bộ
            var linkedUser = await _userRepository.GetUserByIdAsync(studentUserId);
            if (linkedUser != null)
            {
                linkedUser.Avatarurl = newUrl;
                await _userRepository.UpdateUserAsync(linkedUser);
            }

            await _dbContext.SaveChangesAsync();

            return MapToResponse(student);
        }

        public async Task<StudentProfileResponse> UpdateSelfProfileAsync(string studentUserId, UpdateStudentRequest request)
        {
            ValidateBirthdate(request.Birthdate);

            // Load cả 2 entity trước khi modify
            var student = await _studentRepository.FindByStudentOrLinkedUserAsync(studentUserId)
                ?? throw new StudentNotFoundException();

            var linkedUser = await _userRepository.GetUserByIdAsync(studentUserId);

            // Đã xác minh CCCD → họ tên & ngày sinh là nguồn chuẩn từ CCCD, KHÔNG cho tự sửa
            var identityLocked = linkedUser?.Isidentityverified == true;

            // Cập nhật Studentprofile
            if (!identityLocked)
                student.Fullname = request.Fullname;
            if (!identityLocked)
                student.Birthdate = request.Birthdate;
            student.School = request.School;
            student.Gradelevelid = request.GradeLevelId;
            student.Learninggoals = request.Learninggoals;

            // Đồng bộ Fullname/Birthdate (nếu chưa bị khóa) sang bảng Users
            if (linkedUser != null && !identityLocked)
            {
                linkedUser.Fullname = request.Fullname;
                linkedUser.Birthdate = request.Birthdate;
            }

            // EF change tracking tự detect thay đổi — không cần gọi Update() thủ công
            await _dbContext.SaveChangesAsync();

            var resp = MapToResponse(student);
            resp.IsIdentityVerified = identityLocked;
            return resp;
        }

        #region Private Helpers

        private async Task<string?> UploadAvatarAsync(IFormFile? avatarFile, string parentId)
        {
            if (avatarFile == null) return null;

            return await _storage.UploadFileAsync(AvatarBucket, parentId, avatarFile);
        }



        private static void ValidateBirthdate(DateOnly? birthdate)
        {
            if (birthdate.HasValue && birthdate.Value > DateOnly.FromDateTime(DateTime.UtcNow))
                throw new InvalidBirthdateException();
        }

        private Task<string> GenerateStudentIdAsync()
            => _studentRepository.GenerateUniqueStudentIdAsync();

        private async Task<string> GenerateUniqueUsernameAsync(string fullName)
        {
            // Tạo base username từ tên: bỏ dấu tiếng Việt, lowercase, nối với nhau
            var baseName = RemoveVietnameseDiacritics(fullName.ToLower().Replace(" ", ""));

            // Chỉ giữ ký tự ASCII alphanumeric
            baseName = new string(baseName.Where(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9')).ToArray());
            if (baseName.Length < 3) baseName = DefaultUsernameBase;
            if (baseName.Length > 15) baseName = baseName[..15];

            for (var i = 0; i < UsernameMaxRetries; i++)
            {
                var randomDigits = RandomNumberGenerator.GetInt32(100, 9999);
                var candidate = $"{baseName}{randomDigits}";
                if (await _userRepository.IsUsernameUniqueAsync(candidate))
                    return candidate;
            }
            throw new InvalidOperationException("Không thể tạo tên đăng nhập duy nhất.");
        }

        /// <summary>
        /// Bỏ dấu tiếng Việt bằng Unicode normalization: ô→o, ă→a, ê→e, ư→u, đ→d, v.v.
        /// </summary>
        private static string RemoveVietnameseDiacritics(string text)
        {
            // Xử lý đ/Đ trước (không phải combining character)
            text = text.Replace("đ", "d").Replace("Đ", "D");

            // Decompose Unicode → base char + combining marks, rồi bỏ combining marks
            var normalized = text.Normalize(System.Text.NormalizationForm.FormD);
            var sb = new System.Text.StringBuilder(normalized.Length);
            foreach (var c in normalized)
            {
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                    != System.Globalization.UnicodeCategory.NonSpacingMark)
                {
                    sb.Append(c);
                }
            }
            return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
        }

        private static string GenerateSecurePassword()
        {
            Span<byte> bytes = stackalloc byte[GeneratedPasswordLength];
            RandomNumberGenerator.Fill(bytes);
            return string.Create(GeneratedPasswordLength, bytes.ToArray(), (chars, b) =>
            {
                for (var i = 0; i < chars.Length; i++)
                    chars[i] = PasswordChars[b[i] % PasswordChars.Length];
            });
        }

        private static StudentProfileResponse MapToResponse(Studentprofile s) => new()
        {
            StudentId = s.Studentid,
            ParentId = s.Parentid,
            FullName = s.Fullname ?? string.Empty,
            BirthDate = s.Birthdate,
            School = s.School,
            GradeLevelId = s.Gradelevelid,
            GradeLevel = s.Gradelevel,
            GradeLevelInfo = s.GradelevelNavigation == null ? null : new GradeLevelResponse
            {
                GradeLevelId = s.GradelevelNavigation.Gradelevelid,
                GradeName = s.GradelevelNavigation.Gradename,
                LevelOrder = s.GradelevelNavigation.Levelorder,
                IsActive = s.GradelevelNavigation.IsActive
            },
            LearningGoals = s.Learninggoals,
            AvatarURL = s.Avatarurl,
            StudentCode = s.Studentcode,
            StudentCodeExpiresAt = s.Studentcodeexpiresat,
            ParentPhone = s.Parentphone,
            CreatedAt = s.Createdat
        };

        #endregion
    }
}
