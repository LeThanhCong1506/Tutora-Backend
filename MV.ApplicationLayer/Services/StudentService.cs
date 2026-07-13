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
    public class StudentService : IStudentService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IFileStorageService _storage;
        private readonly IConfiguration _config;
        private readonly IPasswordRepository _passwordRepository;
        private const int MaxStudentsPerParent = 5;
        private const int LinkCodeLength = 6;
        private const int LinkCodeExpiryHours = 24;
        private const string LinkCodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        private const string AvatarBucket = StorageBucket.Avatars;
        private const int UsernameMaxRetries = 100;
        private const string DefaultUsernameBase = "student";
        private const int GeneratedPasswordLength = 10;
        private const string PasswordChars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789!@#$";

        public StudentService(
            IUnitOfWork unitOfWork,
            IFileStorageService storage,
            IConfiguration config,
            IPasswordRepository passwordRepository)
        {
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _passwordRepository = passwordRepository ?? throw new ArgumentNullException(nameof(passwordRepository));
        }

        public async Task<List<StudentProfileResponse>> GetStudentsByParentIdAsync(string parentId)
        {
            var students = await _unitOfWork.StudentRepository.GetByParentIdAsync(parentId);
            var responses = new List<StudentProfileResponse>();
            foreach (var s in students)
            {
                var resp = MapToResponse(s);
                if (s.Linkeduserid != null)
                {
                    var user = await _unitOfWork.UserRepository.GetUserByIdAsync(s.Linkeduserid);
                    resp.Username = user?.Username;
                }
                responses.Add(resp);
            }
            return responses;
        }

        public async Task<StudentProfileResponse> GetStudentByIdAsync(string studentId, string parentId)
        {
            var student = await _unitOfWork.StudentRepository.GetByIdAndParentAsync(studentId, parentId)
                ?? throw new StudentNotFoundException(studentId);
            var resp = MapToResponse(student);
            if (student.Linkeduserid != null)
            {
                var user = await _unitOfWork.UserRepository.GetUserByIdAsync(student.Linkeduserid);
                resp.Username = user?.Username;
            }
            return resp;
        }

        public async Task<StudentCredentialsResponse> CreateStudentAsync(CreateStudentRequest request, string parentId)
        {
            ValidateBirthdate(request.Birthdate);

            var currentCount = await _unitOfWork.StudentRepository.CountByParentIdAsync(parentId);
            if (currentCount >= MaxStudentsPerParent)
                throw new MaxStudentsReachedException();

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
                Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
            };

            // 4. Lưu cả User và Studentprofile
            await _unitOfWork.UserRepository.CreateUserAsync(childUser);
            await _unitOfWork.StudentRepository.CreateAsync(student);
            await _unitOfWork.SaveChangesAsync();

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
            var student = await _unitOfWork.StudentRepository.GetByIdAndParentAsync(studentId, parentId)
                ?? throw new NotStudentOwnerException(studentId);

            var oldFilePath = student.Avatarurl;
            if (oldFilePath != null)
                await _storage.DeleteFileAsync(AvatarBucket, studentId, oldFilePath);

            student.Avatarurl = await UploadAvatarAsync(avatarFile, parentId);
            _unitOfWork.StudentRepository.Update(student);

            if (student.Linkeduserid != null)
            {
                var linkedUser = await _unitOfWork.UserRepository.GetUserByIdAsync(student.Linkeduserid);
                if (linkedUser != null)
                {
                    linkedUser.Avatarurl = student.Avatarurl;
                    await _unitOfWork.UserRepository.UpdateUserAsync(linkedUser);
                }
            }

            await _unitOfWork.SaveChangesAsync();

            return MapToResponse(student);
        }

        public async Task<StudentProfileResponse> UpdateStudentAsync(string studentId, UpdateStudentRequest request, string parentId)
        {
            ValidateBirthdate(request.Birthdate);

            var student = await _unitOfWork.StudentRepository.GetByIdAndParentAsync(studentId, parentId)
                ?? throw new NotStudentOwnerException(studentId);

            student.Fullname = request.Fullname;
            student.Birthdate = request.Birthdate;
            student.School = request.School;
            student.Gradelevelid = request.GradeLevelId;
            student.Learninggoals = request.Learninggoals;

            _unitOfWork.StudentRepository.Update(student);

            // Cũng cập nhật User.Fullname để đồng bộ tên ở mọi nơi
            if (student.Linkeduserid != null)
            {
                var linkedUser = await _unitOfWork.UserRepository.GetUserByIdAsync(student.Linkeduserid);
                if (linkedUser != null)
                {
                    linkedUser.Fullname = request.Fullname;
                    linkedUser.Birthdate = request.Birthdate;
                    await _unitOfWork.UserRepository.UpdateUserAsync(linkedUser);
                }
            }

            await _unitOfWork.SaveChangesAsync();

            return MapToResponse(student);
        }

        public async Task DeleteStudentAsync(string studentId, string parentId)
        {
            var student = await _unitOfWork.StudentRepository.GetByIdAndParentAsync(studentId, parentId)
                ?? throw new NotStudentOwnerException(studentId);

            if (await _unitOfWork.StudentRepository.HasActiveBookingAsync(studentId))
                throw new StudentHasActiveBookingException();

            _unitOfWork.StudentRepository.SoftDelete(student);
            await _unitOfWork.SaveChangesAsync();
        }

        public async Task<StudentProfileResponse> GenerateLinkCodeAsync(string studentId, string parentId)
        {
            var student = await _unitOfWork.StudentRepository.GetByIdAndParentAsync(studentId, parentId)
                ?? throw new NotStudentOwnerException(studentId);

            student.Studentcode = await GenerateUniqueLinkCodeAsync();
            student.Studentcodeexpiresat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow.AddHours(LinkCodeExpiryHours);

            _unitOfWork.StudentRepository.Update(student);
            await _unitOfWork.SaveChangesAsync();

            return MapToResponse(student);
        }

        public async Task<StudentProfileResponse> LinkStudentWithCodeAsync(string code, string studentUserId)
        {
            var student = await _unitOfWork.StudentRepository.FindByStudentCodeAsync(code)
                ?? throw new LinkCodeNotFoundException();

            if (!student.Studentcodeexpiresat.HasValue || student.Studentcodeexpiresat < MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow)
                throw new LinkCodeExpiredException();

            if (student.Linkeduserid != null)
                throw new StudentAlreadyLinkedException();

            student.Linkeduserid = studentUserId;
            student.Studentcode = null;
            student.Studentcodeexpiresat = null;

            _unitOfWork.StudentRepository.Update(student);
            await _unitOfWork.SaveChangesAsync();

            return MapToResponse(student);
        }

        // === Flow 2: Parent Code-based linking ===

        public async Task<string> GenerateParentCodeAsync(string parentId)
        {
            var parent = await _unitOfWork.UserRepository.GetUserByIdAsync(parentId)
                ?? throw new Exception("Không tìm thấy phụ huynh.");

            // Generate unique parent code
            var code = await GenerateUniqueParentCodeAsync();
            parent.Parentcode = code;
            parent.Parentcodeexpiresat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow.AddHours(LinkCodeExpiryHours);

            await _unitOfWork.UserRepository.UpdateUserAsync(parent);
            await _unitOfWork.SaveChangesAsync();

            return code;
        }

        public async Task<StudentProfileResponse> StudentSelfLinkAsync(string parentCode, string studentUserId)
        {
            // 1. Tìm parent bằng code
            var parent = await _unitOfWork.UserRepository.GetUserByParentCodeAsync(parentCode)
                ?? throw new ParentCodeNotFoundException();

            if (!parent.Parentcodeexpiresat.HasValue || parent.Parentcodeexpiresat < MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow)
                throw new ParentCodeExpiredException();

            // 2. Tìm Studentprofile của student (nếu có)
            var studentProfiles = await _unitOfWork.StudentRepository.GetByLinkedUserIdAsync(studentUserId);
            var student = studentProfiles.FirstOrDefault();

            if (student != null)
            {
                // Student đã có profile → kiểm tra đã có parent chưa
                if (student.Parentid != null)
                    throw new StudentAlreadyHasParentException();

                // Gắn parent
                student.Parentid = parent.Userid;
                _unitOfWork.StudentRepository.Update(student);
            }
            else
            {
                // Student tự đăng ký chưa có Studentprofile → auto-create từ User entity
                var user = await _unitOfWork.UserRepository.GetUserByIdAsync(studentUserId)
                    ?? throw new StudentNotFoundException();

                student = new Studentprofile
                {
                    Studentid = await GenerateStudentIdAsync(),
                    Parentid = parent.Userid,
                    Linkeduserid = studentUserId,
                    Fullname = user.Fullname ?? "Học sinh",
                    Avatarurl = user.Avatarurl,
                    Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
                };

                await _unitOfWork.StudentRepository.CreateAsync(student);
            }

            // 3. Clear parent code (đã sử dụng)
            parent.Parentcode = null;
            parent.Parentcodeexpiresat = null;
            await _unitOfWork.UserRepository.UpdateUserAsync(parent);

            await _unitOfWork.SaveChangesAsync();

            return MapToResponse(student);
        }

        /// <summary>
        /// Reset password cho student (chỉ parent sở hữu mới được gọi)
        /// </summary>
        public async Task<StudentCredentialsResponse> ResetStudentPasswordAsync(string studentId, string parentId)
        {
            var student = await _unitOfWork.StudentRepository.GetByIdAndParentAsync(studentId, parentId)
                ?? throw new NotStudentOwnerException(studentId);

            if (string.IsNullOrEmpty(student.Linkeduserid))
                throw new InvalidOperationException("Học sinh chưa có tài khoản liên kết.");

            var user = await _unitOfWork.UserRepository.GetUserByIdAsync(student.Linkeduserid)
                ?? throw new InvalidOperationException("Không tìm thấy tài khoản liên kết.");

            // Generate new password
            var newPassword = GenerateSecurePassword();
            user.Password = _passwordRepository.HashPassword(newPassword);

            await _unitOfWork.UserRepository.UpdateUserAsync(user);
            await _unitOfWork.SaveChangesAsync();

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
            var profiles = await _unitOfWork.StudentRepository.GetByLinkedUserIdAsync(studentUserId);
            var student = profiles.FirstOrDefault(p => p.Parentid != null) ?? profiles.FirstOrDefault();

            if (student == null)
            {
                return new StudentLinkStatusResponse { Linked = false };
            }

            var linked = student.Parentid != null;

            string? parentName = null;
            if (linked)
            {
                var parent = await _unitOfWork.UserRepository.GetUserByIdAsync(student.Parentid!);
                parentName = parent?.Fullname;
            }

            return new StudentLinkStatusResponse
            {
                Linked = linked,
                ParentName = parentName,
                ParentId = student.Parentid,
                StudentProfile = MapToResponse(student)
            };
        }

        public async Task<StudentProfileResponse> UpdateSelfAvatarAsync(string studentUserId, IFormFile avatarFile)
        {
            var student = await _unitOfWork.StudentRepository.FindByStudentOrLinkedUserAsync(studentUserId)
                ?? throw new StudentNotFoundException();

            var oldFilePath = student.Avatarurl;
            if (oldFilePath != null)
                await _storage.DeleteFileAsync(AvatarBucket, studentUserId, oldFilePath);

            var newUrl = await _storage.UploadFileAsync(AvatarBucket, studentUserId, avatarFile);
            student.Avatarurl = newUrl;
            _unitOfWork.StudentRepository.Update(student);

            // Cập nhật User để 2 bảng luôn đồng bộ
            var linkedUser = await _unitOfWork.UserRepository.GetUserByIdAsync(studentUserId);
            if (linkedUser != null)
            {
                linkedUser.Avatarurl = newUrl;
                await _unitOfWork.UserRepository.UpdateUserAsync(linkedUser);
            }

            await _unitOfWork.SaveChangesAsync();

            return MapToResponse(student);
        }

        public async Task<StudentProfileResponse> UpdateSelfProfileAsync(string studentUserId, UpdateStudentRequest request)
        {
            ValidateBirthdate(request.Birthdate);

            // Load cả 2 entity trước khi modify
            var student = await _unitOfWork.StudentRepository.FindByStudentOrLinkedUserAsync(studentUserId)
                ?? throw new StudentNotFoundException();

            var linkedUser = await _unitOfWork.UserRepository.GetUserByIdAsync(studentUserId);

            // Cập nhật Studentprofile
            student.Fullname = request.Fullname;
            student.Birthdate = request.Birthdate;
            student.School = request.School;
            student.Gradelevelid = request.GradeLevelId;
            student.Learninggoals = request.Learninggoals;

            // Đồng bộ Fullname + Birthdate sang bảng Users
            if (linkedUser != null)
            {
                linkedUser.Fullname = request.Fullname;
                linkedUser.Birthdate = request.Birthdate;
            }

            // EF change tracking tự detect thay đổi — không cần gọi Update() thủ công
            await _unitOfWork.SaveChangesAsync();

            return MapToResponse(student);
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
            => _unitOfWork.StudentRepository.GenerateUniqueStudentIdAsync();

        private async Task<string> GenerateUniqueLinkCodeAsync()
        {
            var existingCodes = (await _unitOfWork.StudentRepository.GetAllStudentCodesAsync()).ToHashSet();
            for (var i = 0; i < 100; i++)
            {
                var code = GenerateRandomCode(LinkCodeLength, LinkCodeChars);
                if (!existingCodes.Contains(code))
                    return code;
            }
            throw new InvalidOperationException("Không thể tạo mã liên kết duy nhất.");
        }

        private async Task<string> GenerateUniqueParentCodeAsync()
        {
            for (var i = 0; i < 100; i++)
            {
                var code = GenerateRandomCode(LinkCodeLength, LinkCodeChars);
                var existing = await _unitOfWork.UserRepository.GetUserByParentCodeAsync(code);
                if (existing == null)
                    return code;
            }
            throw new InvalidOperationException("Không thể tạo mã phụ huynh duy nhất.");
        }

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
                if (await _unitOfWork.UserRepository.IsUsernameUniqueAsync(candidate))
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

        private static string GenerateRandomCode(int length, string charset)
        {
            Span<byte> bytes = stackalloc byte[length];
            RandomNumberGenerator.Fill(bytes);
            return string.Create(length, bytes.ToArray(), (chars, b) =>
            {
                for (var i = 0; i < chars.Length; i++)
                    chars[i] = charset[b[i] % charset.Length];
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
                LevelOrder = s.GradelevelNavigation.Levelorder
            },
            LearningGoals = s.Learninggoals,
            AvatarURL = s.Avatarurl,
            StudentCode = s.Studentcode,
            StudentCodeExpiresAt = s.Studentcodeexpiresat,
            CreatedAt = s.Createdat
        };

        #endregion
    }
}
