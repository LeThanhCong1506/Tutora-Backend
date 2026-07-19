using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.RepositoryInterfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.Services
{
    /// <summary>
    /// Auth tập trung số điện thoại: đăng ký bằng phone + mật khẩu, bắt buộc verify OTP phone.
    /// Email là tùy chọn. Google/Zalo login đi qua service riêng (LoginService/ZaloAuthService).
    /// </summary>
    public class SimpleAuthService : ISimpleAuthService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IPasswordRepository _passwordRepository;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly IOtpSender _otpSender;
        private readonly IConfiguration _configuration;
        private readonly ILogger<SimpleAuthService> _logger;
        private readonly IDistributedCache _cache;

        private const int OtpExpiryMinutes = 10;
        private const int MaxOtpAttempts = 5;

        public SimpleAuthService(
            IUnitOfWork unitOfWork,
            IPasswordRepository passwordRepository,
            IAuthenticationRepository authenticationRepository,
            IOtpSender otpSender,
            IConfiguration configuration,
            ILogger<SimpleAuthService> logger,
            IDistributedCache cache)
        {
            _unitOfWork = unitOfWork;
            _passwordRepository = passwordRepository;
            _authenticationRepository = authenticationRepository;
            _otpSender = otpSender;
            _configuration = configuration;
            _logger = logger;
            _cache = cache;
        }

        public async Task<TokenResponse> SimpleLoginAsync(SimpleLoginRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.EmailOrPhone) || string.IsNullOrEmpty(request.Password))
                {
                    return new TokenResponse { ErrorMessage = "Bạn cần cung cấp email/số điện thoại và mật khẩu." };
                }

                User? user;

                if (request.EmailOrPhone.Contains("@"))
                {
                    var isValid = await _unitOfWork.UserRepository.CheckIfUserLoginCorrectAsync(
                        request.EmailOrPhone, request.Password);

                    if (!isValid)
                    {
                        return new TokenResponse { ErrorMessage = "Email hoặc mật khẩu không đúng." };
                    }

                    user = await _unitOfWork.UserRepository.GetUserByEmailAsync(request.EmailOrPhone);
                }
                else if (request.EmailOrPhone.All(char.IsDigit) || request.EmailOrPhone.StartsWith("+"))
                {
                    var isValid = await _unitOfWork.UserRepository.CheckIfUserLoginCorrectByPhoneAsync(
                        request.EmailOrPhone, request.Password);

                    if (!isValid)
                    {
                        return new TokenResponse { ErrorMessage = "Số điện thoại hoặc mật khẩu không đúng." };
                    }

                    user = await _unitOfWork.UserRepository.GetUserByPhoneAsync(request.EmailOrPhone);
                }
                else
                {
                    var isValid = await _unitOfWork.UserRepository.CheckIfUserLoginCorrectByUsernameAsync(
                        request.EmailOrPhone, request.Password);

                    if (!isValid)
                    {
                        return new TokenResponse { ErrorMessage = "Tên người dùng hoặc mật khẩu không chính xác." };
                    }

                    user = await _unitOfWork.UserRepository.GetUserByUsernameAsync(request.EmailOrPhone);
                }

                if (user == null)
                {
                    return new TokenResponse { ErrorMessage = "Không tìm thấy người dùng." };
                }

                if (user.Status == 0)
                {
                    return new TokenResponse { ErrorMessage = "Tài khoản đã bị khóa." };
                }

                // Cổng xác thực SĐT là cơ chế onboarding chống-ảo cho KHÁCH HÀNG
                // (tự đăng ký). Tài khoản nội bộ (Staff/Admin) do Admin cấp — phone
                // là tùy chọn khi tạo — nên miễn cả 2 cổng; nếu không, staff không
                // phone sẽ vĩnh viễn bị chặn đăng nhập, còn có phone thì bị ép OTP
                // Zalo như khách hàng.
                var isInternalAccount = UserRole.IsInternal(user.Primaryrole);

                if (!isInternalAccount && string.IsNullOrWhiteSpace(user.Phone))
                {
                    return new TokenResponse
                    {
                        ErrorMessage = "Tài khoản chưa có số điện thoại. Vui lòng bổ sung và xác thực số điện thoại trước khi đăng nhập.",
                        RequiresPhoneInput = true
                    };
                }

                if (!isInternalAccount && user.Isphoneverified != true)
                {
                    return new TokenResponse
                    {
                        ErrorMessage = "Số điện thoại chưa được xác thực. Vui lòng xác thực OTP trước khi đăng nhập.",
                        RequiresPhoneVerification = true,
                        Phone = user.Phone
                    };
                }

                await _unitOfWork.UserRepository.UpdateLastLoginAtAsync(user.Userid, MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow);
                await _unitOfWork.SaveChangesAsync();

                return await CreateTokenResponseAsync(user);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while logging in");
                return new TokenResponse { ErrorMessage = $"Error: {ex.Message}" };
            }
        }

        public async Task<TokenResponse> SimpleRegisterAsync(SimpleRegisterRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Phone))
                {
                    return new TokenResponse { ErrorMessage = "Số điện thoại là bắt buộc." };
                }

                if (string.IsNullOrEmpty(request.Password))
                {
                    return new TokenResponse { ErrorMessage = "Mật khẩu là bắt buộc." };
                }

                if (string.IsNullOrEmpty(request.FullName))
                {
                    return new TokenResponse { ErrorMessage = "Tên đầy đủ là bắt buộc." };
                }

                var requestedRole = !string.IsNullOrEmpty(request.Role) ? request.Role : UserRole.Parent;
                if (!UserRole.SelfRegisterable.Contains(requestedRole))
                {
                    return new TokenResponse { ErrorMessage = "Chức vụ này không cho phép tự đăng ký." };
                }

                var phone = request.Phone.Trim();

                // Email tùy chọn — nếu có thì kiểm tra trùng.
                if (!string.IsNullOrWhiteSpace(request.Email))
                {
                    var existingByEmail = await _unitOfWork.UserRepository.GetUserByEmailAsync(request.Email);
                    if (existingByEmail != null)
                    {
                        return new TokenResponse { ErrorMessage = "Email đã tồn tại." };
                    }
                }

                var existingUserByPhone = await _unitOfWork.UserRepository.GetUserByPhoneAsync(phone);

                // SĐT đã tồn tại và đã xác thực → chặn.
                if (existingUserByPhone != null && existingUserByPhone.Isphoneverified == true)
                {
                    return new TokenResponse { ErrorMessage = "Số điện thoại đã được sử dụng." };
                }

                // SĐT đã có nhưng CHƯA xác thực (đăng ký dở) → cập nhật lại thông tin + gửi lại OTP.
                if (existingUserByPhone != null)
                {
                    existingUserByPhone.Password = _passwordRepository.HashPassword(request.Password);
                    existingUserByPhone.Fullname = request.FullName;
                    if (!string.IsNullOrWhiteSpace(request.Email))
                    {
                        existingUserByPhone.Email = request.Email;
                    }

                    // Lần đăng ký dở trước có thể đã tạo Studentprofile → cập nhật tên cho khớp bảng Users.
                    var existingProfile = await _unitOfWork.StudentRepository
                        .FindByStudentOrLinkedUserAsync(existingUserByPhone.Userid);
                    if (existingProfile != null)
                        existingProfile.Fullname = request.FullName;

                    await _unitOfWork.UserRepository.UpdateUserAsync(existingUserByPhone);
                    await _unitOfWork.SaveChangesAsync();

                    var resendCode = GenerateOtpCode();
                    await StoreOtpAsync(PhoneVerifyKey(phone), resendCode);
                    await _otpSender.SendOtpAsync(phone, resendCode);

                    return new TokenResponse
                    {
                        RequiresPhoneVerification = true,
                        Phone = phone,
                        ErrorMessage = string.Empty
                    };
                }

                var userId = Guid.NewGuid().ToString();
                var newUser = new User
                {
                    Userid = userId,
                    Email = request.Email,
                    Phone = phone,
                    Password = _passwordRepository.HashPassword(request.Password),
                    Fullname = request.FullName,
                    Status = 1,
                    Isemailverified = false,
                    Isphoneverified = false,
                    Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow,
                    Primaryrole = requestedRole
                };

                if (string.Equals(requestedRole, UserRole.Tutor, StringComparison.OrdinalIgnoreCase))
                {
                    newUser.Tutorprofile = new Tutorprofile
                    {
                        Tutorid = userId,
                        Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow,
                        Profilestatus = TutorProfileStatus.Draft
                    };
                }
                else if (string.Equals(requestedRole, UserRole.Student, StringComparison.OrdinalIgnoreCase))
                {
                    newUser.StudentprofileLinkedusers.Add(new Studentprofile
                    {
                        Studentid = userId,
                        Parentid = null,
                        Fullname = request.FullName,
                        // SĐT phụ huynh (tùy chọn) — chỉ để gửi ZNS theo dõi.
                        Parentphone = string.IsNullOrWhiteSpace(request.ParentPhone) ? null : request.ParentPhone.Trim(),
                        Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
                    });
                }
                else if (string.Equals(requestedRole, UserRole.Parent, StringComparison.OrdinalIgnoreCase))
                {
                    newUser.Wallet = new Wallet
                    {
                        Userid = userId,
                        Balance = 0,
                        Lastupdated = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
                    };
                }

                await _unitOfWork.UserRepository.CreateUserAsync(newUser);
                await _unitOfWork.SaveChangesAsync();

                var otpCode = GenerateOtpCode();
                await StoreOtpAsync(PhoneVerifyKey(phone), otpCode);
                await _otpSender.SendOtpAsync(phone, otpCode);

                return new TokenResponse
                {
                    RequiresPhoneVerification = true,
                    Phone = phone,
                    ErrorMessage = string.Empty
                };
            }
            catch (DbUpdateException ex) when (IsUniquePhoneConflict(ex))
            {
                _logger.LogWarning(ex, "Duplicate phone while registering");
                return new TokenResponse { ErrorMessage = "Số điện thoại đã được sử dụng." };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while registering");
                return new TokenResponse { ErrorMessage = $"Error: {ex.Message}" };
            }
        }

        public async Task<TokenResponse> VerifyPhoneOtpAsync(VerifyPhoneOtpRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Phone) || string.IsNullOrWhiteSpace(request.Otp))
                {
                    return new TokenResponse { ErrorMessage = "Số điện thoại và OTP là bắt buộc." };
                }

                var phone = request.Phone.Trim();
                var user = await _unitOfWork.UserRepository.GetUserByPhoneAsync(phone);
                if (user == null)
                {
                    return new TokenResponse { ErrorMessage = "Không tìm thấy người dùng." };
                }

                if (user.Isphoneverified == true)
                {
                    return await CreateTokenResponseAsync(user);
                }

                var otpEntry = await GetOtpAsync(PhoneVerifyKey(phone));
                if (otpEntry == null)
                {
                    return new TokenResponse { ErrorMessage = "OTP đã hết hạn. Vui lòng gửi lại mã mới." };
                }

                if (otpEntry.Attempts >= MaxOtpAttempts)
                {
                    return new TokenResponse { ErrorMessage = "Quá nhiều lần nhập OTP không hợp lệ. Vui lòng gửi lại mã mới." };
                }

                if (!string.Equals(otpEntry.Code, request.Otp.Trim(), StringComparison.Ordinal))
                {
                    otpEntry.Attempts++;
                    await SaveOtpAsync(PhoneVerifyKey(phone), otpEntry);
                    return new TokenResponse { ErrorMessage = "Mã OTP không hợp lệ." };
                }

                user.Isphoneverified = true;
                await _unitOfWork.UserRepository.UpdateUserAsync(user);
                await _unitOfWork.SaveChangesAsync();
                await RemoveOtpAsync(PhoneVerifyKey(phone));

                return await CreateTokenResponseAsync(user);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while verifying phone OTP");
                return new TokenResponse { ErrorMessage = $"Error: {ex.Message}" };
            }
        }

        public async Task<TokenResponse> ResendPhoneOtpAsync(ResendPhoneOtpRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Phone))
                {
                    return new TokenResponse { ErrorMessage = "Số điện thoại là bắt buộc." };
                }

                var phone = request.Phone.Trim();
                var user = await _unitOfWork.UserRepository.GetUserByPhoneAsync(phone);
                if (user == null)
                {
                    return new TokenResponse { ErrorMessage = "Không tìm thấy người dùng." };
                }

                if (user.Isphoneverified == true)
                {
                    return new TokenResponse { ErrorMessage = "Số điện thoại đã được xác thực." };
                }

                var otpCode = GenerateOtpCode();
                await StoreOtpAsync(PhoneVerifyKey(phone), otpCode);
                await _otpSender.SendOtpAsync(phone, otpCode);

                return new TokenResponse
                {
                    RequiresPhoneVerification = true,
                    Phone = phone,
                    ErrorMessage = string.Empty
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while resending phone OTP");
                return new TokenResponse { ErrorMessage = $"Error: {ex.Message}" };
            }
        }

        public async Task<TokenResponse> ForgotPasswordAsync(ForgotPasswordRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Phone))
                {
                    return new TokenResponse { ErrorMessage = "Số điện thoại là bắt buộc." };
                }

                var phone = request.Phone.Trim();
                var user = await _unitOfWork.UserRepository.GetUserByPhoneAsync(phone);

                // Chỉ gửi OTP nếu user tồn tại; luôn trả success để tránh dò số điện thoại (enumeration).
                if (user != null)
                {
                    var otpCode = GenerateOtpCode();
                    await StoreOtpAsync(PhoneResetKey(phone), otpCode);
                    await _otpSender.SendOtpAsync(phone, otpCode);
                }

                return new TokenResponse { Phone = phone, ErrorMessage = string.Empty };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while processing forgot password (phone)");
                return new TokenResponse { ErrorMessage = $"Error: {ex.Message}" };
            }
        }

        public async Task<TokenResponse> ResetPasswordAsync(ResetPasswordRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Phone) || string.IsNullOrWhiteSpace(request.Otp)
                    || string.IsNullOrWhiteSpace(request.NewPassword))
                {
                    return new TokenResponse { ErrorMessage = "Số điện thoại, OTP và mật khẩu mới là bắt buộc." };
                }

                var phone = request.Phone.Trim();
                var user = await _unitOfWork.UserRepository.GetUserByPhoneAsync(phone);
                if (user == null)
                {
                    return new TokenResponse { ErrorMessage = "Yêu cầu không hợp lệ." };
                }

                var otpEntry = await GetOtpAsync(PhoneResetKey(phone));
                if (otpEntry == null)
                {
                    return new TokenResponse { ErrorMessage = "OTP đã hết hạn. Vui lòng yêu cầu lại." };
                }

                if (otpEntry.Attempts >= MaxOtpAttempts)
                {
                    return new TokenResponse { ErrorMessage = "Quá nhiều lần nhập OTP không hợp lệ. Vui lòng yêu cầu lại." };
                }

                if (!string.Equals(otpEntry.Code, request.Otp.Trim(), StringComparison.Ordinal))
                {
                    otpEntry.Attempts++;
                    await SaveOtpAsync(PhoneResetKey(phone), otpEntry);
                    return new TokenResponse { ErrorMessage = "Mã OTP không hợp lệ." };
                }

                user.Password = _passwordRepository.HashPassword(request.NewPassword);
                await _unitOfWork.UserRepository.UpdateUserAsync(user);
                await _unitOfWork.SaveChangesAsync();
                await RemoveOtpAsync(PhoneResetKey(phone));

                return new TokenResponse { ErrorMessage = string.Empty };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while resetting password (phone)");
                return new TokenResponse { ErrorMessage = $"Error: {ex.Message}" };
            }
        }

        private async Task<TokenResponse> CreateTokenResponseAsync(User user)
        {
            // Chốt chặn cuối trước khi phát token — miễn cho tài khoản nội bộ
            // (Staff/Admin, xác thực bằng email + mật khẩu, không qua OTP).
            if (!UserRole.IsInternal(user.Primaryrole)
                && (string.IsNullOrWhiteSpace(user.Phone) || user.Isphoneverified != true))
            {
                return new TokenResponse
                {
                    ErrorMessage = "Tài khoản phải có số điện thoại đã xác thực trước khi nhận token.",
                    RequiresPhoneInput = string.IsNullOrWhiteSpace(user.Phone),
                    RequiresPhoneVerification = !string.IsNullOrWhiteSpace(user.Phone),
                    Phone = user.Phone
                };
            }

            var role = await _unitOfWork.UserRepository.GetUserRoleByIdAsync(user.Userid);
            if (string.IsNullOrEmpty(role))
            {
                return new TokenResponse { ErrorMessage = "User role not found." };
            }

            var loginResponse = new LoginResponse
            {
                Userid = user.Userid,
                Username = user.Username ?? "",
                Fullname = user.Fullname,
                Email = user.Email ?? "",
                Phone = user.Phone ?? "",
                Role = role,
                Status = user.Status
            };

            var accessToken = _authenticationRepository.GenerateJwtToken(loginResponse);
            var rawRefreshToken = await CreateRefreshTokenAsync(user.Userid);

            return new TokenResponse
            {
                AccessToken = accessToken,
                RefreshToken = rawRefreshToken,
                ErrorMessage = string.Empty
            };
        }

        private async Task<string> CreateRefreshTokenAsync(string userId)
        {
            var rawToken = _authenticationRepository.GenerateRefreshToken();
            var tokenHash = _authenticationRepository.HashToken(rawToken);
            var expiryDays = int.TryParse(_configuration[ConfigurationKeys.Jwt.RefreshTokenExpiryDays], out var days) ? days : 7;

            var refreshToken = new RefreshToken
            {
                Id = Guid.NewGuid().ToString(),
                Tokenhash = tokenHash,
                Userid = userId,
                Tokenfamily = Guid.NewGuid().ToString(),
                Expiresat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow.AddDays(expiryDays),
                Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
            };

            await _unitOfWork.RefreshTokenRepository.CreateAsync(refreshToken);
            await _unitOfWork.SaveChangesAsync();
            return rawToken;
        }

        private static string GenerateOtpCode()
            => RandomNumberGenerator.GetInt32(100000, 1000000).ToString();

        // ─── OTP storage (Redis via IDistributedCache), keyed by purpose+phone ───
        // Tách 2 namespace key để OTP verify-phone và OTP reset-password không đè nhau.
        private static string PhoneVerifyKey(string phone) => $"otp:phone:{phone.Trim()}";
        private static string PhoneResetKey(string phone) => $"otp:pwdreset:{phone.Trim()}";

        private sealed class OtpEntry
        {
            public string Code { get; set; } = "";
            public int Attempts { get; set; }
            public DateTime ExpiresAtUtc { get; set; }
        }

        private Task StoreOtpAsync(string key, string code)
        {
            var entry = new OtpEntry
            {
                Code = code,
                Attempts = 0,
                ExpiresAtUtc = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow.AddMinutes(OtpExpiryMinutes)
            };
            return SaveOtpAsync(key, entry);
        }

        // Re-save với CÙNG absolute expiry để lần nhập sai không kéo dài tuổi thọ OTP.
        private Task SaveOtpAsync(string key, OtpEntry entry)
        {
            var json = JsonSerializer.Serialize(entry);
            var ttl = entry.ExpiresAtUtc - MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
            if (ttl <= TimeSpan.Zero) ttl = TimeSpan.FromSeconds(1);
            return _cache.SetStringAsync(key, json, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ttl
            });
        }

        private async Task<OtpEntry?> GetOtpAsync(string key)
        {
            var json = await _cache.GetStringAsync(key);
            return string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<OtpEntry>(json);
        }

        private Task RemoveOtpAsync(string key) => _cache.RemoveAsync(key);

        private static bool IsUniquePhoneConflict(DbUpdateException ex)
        {
            var message = $"{ex.Message} {ex.InnerException?.Message}";
            return message.Contains("users_phone_key", StringComparison.OrdinalIgnoreCase);
        }
    }
}
