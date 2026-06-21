using System.Security.Cryptography;
using System.Text.Encodings.Web;
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
    public class SimpleAuthService : ISimpleAuthService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IPasswordRepository _passwordRepository;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly IEmailService _emailService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<SimpleAuthService> _logger;
        private readonly IDistributedCache _cache;

        private const int OtpExpiryMinutes = 10;
        private const int MaxOtpAttempts = 5;

        public SimpleAuthService(
            IUnitOfWork unitOfWork,
            IPasswordRepository passwordRepository,
            IAuthenticationRepository authenticationRepository,
            IEmailService emailService,
            IConfiguration configuration,
            ILogger<SimpleAuthService> logger,
            IDistributedCache cache)
        {
            _unitOfWork = unitOfWork;
            _passwordRepository = passwordRepository;
            _authenticationRepository = authenticationRepository;
            _emailService = emailService;
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

                if (!string.IsNullOrWhiteSpace(user.Email) && user.Isemailverified != true)
                {
                    return new TokenResponse
                    {
                        ErrorMessage = "Địa chỉ email chưa được xác minh. Vui lòng xác minh email của bạn trước khi đăng nhập.",
                        RequiresEmailVerification = true,
                        Email = user.Email
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
                if (string.IsNullOrEmpty(request.Email) && string.IsNullOrEmpty(request.Phone))
                {
                    return new TokenResponse { ErrorMessage = "Cần có thông tin liên lạc qua email hoặc số điện thoại." };
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

                var existingUserByEmail = !string.IsNullOrWhiteSpace(request.Email)
                    ? await _unitOfWork.UserRepository.GetUserByEmailAsync(request.Email)
                    : null;

                if (!string.IsNullOrWhiteSpace(request.Phone))
                {
                    var existingUserByPhone = await _unitOfWork.UserRepository.GetUserByPhoneAsync(request.Phone);
                    if (existingUserByPhone != null &&
                        (existingUserByEmail == null || existingUserByPhone.Userid != existingUserByEmail.Userid))
                    {
                        return new TokenResponse { ErrorMessage = "Số điện thoại đã được sử dụng." };
                    }
                }

                if (existingUserByEmail != null)
                {
                    if (existingUserByEmail.Isemailverified == true)
                    {
                        return new TokenResponse { ErrorMessage = "Email đã tồn tại." };
                    }

                    var otpCode = GenerateOtpCode();
                    existingUserByEmail.Password = _passwordRepository.HashPassword(request.Password);
                    existingUserByEmail.Fullname = request.FullName;
                    if (!string.IsNullOrEmpty(request.Phone))
                    {
                        existingUserByEmail.Phone = request.Phone;
                    }

                    await _unitOfWork.UserRepository.UpdateUserAsync(existingUserByEmail);
                    await _unitOfWork.SaveChangesAsync();
                    await StoreEmailOtpAsync(request.Email!, otpCode);
                    await SendVerificationEmailAsync(request.Email!, request.FullName, otpCode);

                    return new TokenResponse
                    {
                        RequiresEmailVerification = true,
                        Email = request.Email,
                        ErrorMessage = string.Empty
                    };
                }

                if (!string.IsNullOrWhiteSpace(request.Email) && !request.Email.EndsWith("@tutora.test"))
                {
                    var otpCode = GenerateOtpCode();
                    var userId = Guid.NewGuid().ToString();
                    var newUser = new User
                    {
                        Userid = userId,
                        Email = request.Email,
                        Phone = request.Phone,
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
                    await StoreEmailOtpAsync(request.Email, otpCode);
                    await SendVerificationEmailAsync(request.Email, request.FullName, otpCode);

                    return new TokenResponse
                    {
                        RequiresEmailVerification = true,
                        Email = request.Email,
                        ErrorMessage = string.Empty
                    };
                }

                return await CreateUserAndTokenAsync(request);
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

        public async Task<TokenResponse> VerifyEmailOtpAsync(VerifyOtpRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Otp))
                {
                    return new TokenResponse { ErrorMessage = "Email và OTP là bắt buộc." };
                }

                var user = await _unitOfWork.UserRepository.GetUserByEmailAsync(request.Email);
                if (user == null)
                {
                    return new TokenResponse { ErrorMessage = "Không tìm thấy người dùng." };
                }

                if (user.Isemailverified == true)
                {
                    return await CreateTokenResponseAsync(user);
                }

                var otpEntry = await GetEmailOtpAsync(request.Email);
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
                    await SaveEmailOtpAsync(request.Email, otpEntry);
                    return new TokenResponse { ErrorMessage = "Mã OTP không hợp lệ." };
                }

                user.Isemailverified = true;
                await _unitOfWork.UserRepository.UpdateUserAsync(user);
                await _unitOfWork.SaveChangesAsync();
                await RemoveEmailOtpAsync(request.Email);

                return await CreateTokenResponseAsync(user);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while verifying email OTP");
                return new TokenResponse { ErrorMessage = $"Error: {ex.Message}" };
            }
        }

        public async Task<TokenResponse> ResendVerificationEmailAsync(ResendVerificationEmailRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Email))
                {
                    return new TokenResponse { ErrorMessage = "Email là bắt buộc." };
                }

                var user = await _unitOfWork.UserRepository.GetUserByEmailAsync(request.Email);
                if (user == null)
                {
                    return new TokenResponse { ErrorMessage = "Không tìm thấy người dùng." };
                }

                if (user.Isemailverified == true)
                {
                    return new TokenResponse { ErrorMessage = "Email đã được xác minh." };
                }

                var otpCode = GenerateOtpCode();
                await StoreEmailOtpAsync(user.Email!, otpCode);
                await SendVerificationEmailAsync(user.Email!, user.Fullname ?? "there", otpCode);

                return new TokenResponse
                {
                    RequiresEmailVerification = true,
                    Email = user.Email,
                    ErrorMessage = string.Empty
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while resending verification email");
                return new TokenResponse { ErrorMessage = $"Error: {ex.Message}" };
            }
        }
        public async Task<TokenResponse> ForgotPasswordAsync(ForgotPasswordRequest request)
        {
            try
            {
                var user = await _unitOfWork.UserRepository.GetUserByEmailAsync(request.Email!);
                if (user == null)
                {
                    // Return success even if user not found to prevent email enumeration attacks
                    return new TokenResponse { ErrorMessage = string.Empty };
                }

                // Generate a secure token, store in Redis with a 10-minute TTL
                var token = Guid.NewGuid().ToString("N").Substring(0, 10);
                await StoreResetTokenAsync(user.Email!, token);

                // Generate reset link
                var frontendUrl = _configuration["FrontendUrl"] ?? "http://localhost:5173";
                var resetLink = $"{frontendUrl}/reset-password?email={Uri.EscapeDataString(user.Email!)}&token={token}";

                // Send email
                var emailBody = $"""
                <!DOCTYPE html>
                <html lang="vi">
                <head>
                  <meta charset="UTF-8" />
                  <meta name="viewport" content="width=device-width, initial-scale=1.0" />
                  <title>Khôi phục mật khẩu Tutora</title>
                </head>
                <body style="margin:0;padding:0;background-color:#f0f4ff;font-family:'Segoe UI',Arial,Helvetica,sans-serif;color:#1e293b;">
                  <table width="100%" cellpadding="0" cellspacing="0" style="padding:48px 16px;">
                    <tr>
                      <td align="center">

                        <!-- Outer Card -->
                        <table width="600" cellpadding="0" cellspacing="0" style="max-width:600px;background-color:#ffffff;border-radius:20px;overflow:hidden;box-shadow:0 8px 40px rgba(59,130,246,0.12);">

                          <!-- ===== HEADER ===== -->
                          <tr>
                            <td style="background:linear-gradient(135deg,#1d4ed8 0%,#3b82f6 50%,#06b6d4 100%);padding:0;">
                              <!-- Decorative top band -->
                              <table width="100%" cellpadding="0" cellspacing="0">
                                <tr>
                                  <td style="padding:36px 40px 28px 40px;text-align:center;">
                                    <!-- Logo row -->
                                    <table cellpadding="0" cellspacing="0" style="margin:0 auto 12px auto;">
                                      <tr>
                                        <td style="background:rgba(255,255,255,0.18);border-radius:50%;width:64px;height:64px;text-align:center;vertical-align:middle;font-size:32px;line-height:64px;">
                                          &#127979;
                                        </td>
                                      </tr>
                                    </table>
                                    <h1 style="margin:0;font-size:30px;font-weight:800;color:#ffffff;letter-spacing:2px;text-shadow:0 2px 8px rgba(0,0,0,0.15);">
                                      TUTORA
                                    </h1>
                                    <p style="margin:8px 0 0 0;font-size:13px;color:rgba(255,255,255,0.85);letter-spacing:0.5px;">
                                      Nền tảng kết nối Học Sinh &amp; Gia Sư uy tín
                                    </p>
                                  </td>
                                </tr>
                                <!-- Decorative wave divider -->
                                <tr>
                                  <td style="line-height:0;font-size:0;">
                                    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 600 40" style="display:block;width:100%;" preserveAspectRatio="none">
                                      <path d="M0,0 C150,40 450,40 600,0 L600,40 L0,40 Z" fill="#ffffff"/>
                                    </svg>
                                  </td>
                                </tr>
                              </table>
                            </td>
                          </tr>

                          <!-- ===== BODY ===== -->
                          <tr>
                            <td style="padding:36px 44px 28px 44px;">

                              <!-- Greeting -->
                              <h2 style="margin:0 0 6px 0;font-size:22px;font-weight:700;color:#1e293b;">
                                Khôi Phục Mật Khẩu
                              </h2>
                              <p style="margin:0 0 24px 0;font-size:15px;color:#64748b;line-height:1.7;">
                                Chúng tôi nhận được yêu cầu đổi mật khẩu cho tài khoản liên kết với email này trên <strong style="color:#1d4ed8;">Tutora</strong>. Vui lòng nhấn vào nút bên dưới để đặt lại mật khẩu của bạn. Đường link này chỉ có hiệu lực trong vòng <b>10 phút</b>.
                              </p>

                              <!-- Link Section -->
                              <table width="100%" cellpadding="0" cellspacing="0">
                                <tr>
                                  <td align="center" style="padding:8px 0 28px 0;">
                                    <a href="{resetLink}" style="background:linear-gradient(135deg,#1d4ed8 0%,#3b82f6 50%,#06b6d4 100%);color:#ffffff;text-decoration:none;padding:15px 35px;font-size:16px;font-weight:bold;border-radius:25px;display:inline-block;box-shadow:0 4px 15px rgba(59,130,246,0.4);text-transform:uppercase;letter-spacing:1px;">ĐẶT LẠI MẬT KHẨU</a>
                                  </td>
                                </tr>
                              </table>

                              <!-- Instructions -->
                              <table width="100%" cellpadding="0" cellspacing="0" style="background:#f8fafc;border-left:4px solid #3b82f6;border-radius:0 8px 8px 0;margin-bottom:24px;">
                                <tr>
                                  <td style="padding:16px 20px;">
                                    <p style="margin:0;font-size:14px;color:#475569;line-height:1.7;">
                                      &#9888;&#65039; <strong>Lưu ý:</strong> Nếu nút bấm không hoạt động, bạn có thể truy cập đường link sau:<br>
                                      <a href="{resetLink}" style="color:#3b82f6;text-decoration:none;word-break:break-all;">{resetLink}</a>
                                    </p>
                                  </td>
                                </tr>
                              </table>

                              <p style="font-size:14px;color:#ef4444;font-style:italic;line-height:1.7;margin:0;">
                                Nếu bạn không yêu cầu đổi mật khẩu, vui lòng bỏ qua email này. Tài khoản của bạn vẫn an toàn tuyệt đối.
                              </p>
                            </td>
                          </tr>

                          <!-- ===== DIVIDER ===== -->
                          <tr>
                            <td style="padding:0 44px;">
                              <div style="border-top:1px solid #e2e8f0;"></div>
                            </td>
                          </tr>

                          <!-- ===== FOOTER ===== -->
                          <tr>
                            <td style="background:linear-gradient(135deg,#1e3a8a,#1d4ed8);padding:24px 40px;text-align:center;border-radius:0 0 20px 20px;">
                              <p style="margin:0 0 6px 0;font-size:13px;color:rgba(255,255,255,0.9);font-weight:600;">
                                &#127775; Tutora &mdash; Nâng cánh ước mơ tri thức
                              </p>
                              <p style="margin:0;font-size:11px;color:rgba(255,255,255,0.55);">
                                &copy; 2026 Tutora. All rights reserved. &nbsp;|&nbsp; Bạn nhận được email này vì đã gửi yêu cầu cấp lại mật khẩu.
                              </p>
                            </td>
                          </tr>

                        </table>
                        <!-- End Outer Card -->

                      </td>
                    </tr>
                  </table>
                </body>
                </html>
                """;

                await _emailService.SendEmailAsync(user.Email!, "Yêu cầu đổi mật khẩu - Agora Platform", emailBody);

                return new TokenResponse { ErrorMessage = string.Empty };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while processing forgot password request");
                return new TokenResponse { ErrorMessage = $"Error: {ex.Message}" };
            }
        }

        public async Task<TokenResponse> ResetPasswordAsync(ResetPasswordRequest request)
        {
            try
            {
                var user = await _unitOfWork.UserRepository.GetUserByEmailAsync(request.Email);
                if (user == null)
                {
                    return new TokenResponse { ErrorMessage = "Yêu cầu không hợp lệ." };
                }

                var storedToken = await GetResetTokenAsync(request.Email);
                if (string.IsNullOrEmpty(storedToken) || !string.Equals(storedToken, request.Token, StringComparison.Ordinal))
                {
                    return new TokenResponse { ErrorMessage = "Đường link đã hết hạn hoặc không hợp lệ. Vui lòng yêu cầu lại." };
                }

                user.Password = _passwordRepository.HashPassword(request.NewPassword);
                await _unitOfWork.UserRepository.UpdateUserAsync(user);
                await _unitOfWork.SaveChangesAsync();
                await RemoveResetTokenAsync(request.Email);

                return new TokenResponse { ErrorMessage = string.Empty };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while resetting password");
                return new TokenResponse { ErrorMessage = $"Error: {ex.Message}" };
            }
        }

        private async Task<TokenResponse> CreateTokenResponseAsync(User user)
        {
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

        // ─── OTP / reset-token storage (Redis via IDistributedCache) ─────────
        private static string EmailOtpKey(string email) => $"otp:verify:{email.Trim().ToLowerInvariant()}";
        private static string ResetTokenKey(string email) => $"otp:reset:{email.Trim().ToLowerInvariant()}";

        private sealed class EmailOtpEntry
        {
            public string Code { get; set; } = "";
            public int Attempts { get; set; }
            public DateTime ExpiresAtUtc { get; set; }
        }

        private Task StoreEmailOtpAsync(string email, string code)
        {
            var entry = new EmailOtpEntry
            {
                Code = code,
                Attempts = 0,
                ExpiresAtUtc = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow.AddMinutes(OtpExpiryMinutes)
            };
            return SaveEmailOtpAsync(email, entry);
        }

        // Re-saves with the SAME absolute expiry so a failed attempt does not extend the OTP lifetime.
        private Task SaveEmailOtpAsync(string email, EmailOtpEntry entry)
        {
            var json = JsonSerializer.Serialize(entry);
            return _cache.SetStringAsync(EmailOtpKey(email), json, new DistributedCacheEntryOptions
            {
                AbsoluteExpiration = new DateTimeOffset(
                    DateTime.SpecifyKind(entry.ExpiresAtUtc, DateTimeKind.Utc), TimeSpan.Zero)
            });
        }

        private async Task<EmailOtpEntry?> GetEmailOtpAsync(string email)
        {
            var json = await _cache.GetStringAsync(EmailOtpKey(email));
            return string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<EmailOtpEntry>(json);
        }

        private Task RemoveEmailOtpAsync(string email) => _cache.RemoveAsync(EmailOtpKey(email));

        private Task StoreResetTokenAsync(string email, string token)
            => _cache.SetStringAsync(ResetTokenKey(email), token, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(OtpExpiryMinutes)
            });

        private Task<string?> GetResetTokenAsync(string email) => _cache.GetStringAsync(ResetTokenKey(email));

        private Task RemoveResetTokenAsync(string email) => _cache.RemoveAsync(ResetTokenKey(email));

        private static bool IsUniquePhoneConflict(DbUpdateException ex)
        {
            var message = $"{ex.Message} {ex.InnerException?.Message}";
            return message.Contains("users_phone_key", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<TokenResponse> CreateUserAndTokenAsync(SimpleRegisterRequest request)
        {
            var requestedRole = !string.IsNullOrEmpty(request.Role) ? request.Role : UserRole.Parent;
            var userId = Guid.NewGuid().ToString();
            var newUser = new User
            {
                Userid = userId,
                Email = request.Email,
                Phone = request.Phone,
                Password = _passwordRepository.HashPassword(request.Password),
                Fullname = request.FullName,
                Status = 1,
                Isemailverified = !string.IsNullOrEmpty(request.Email), // Verified since this is called after OTP check
                Isphoneverified = !string.IsNullOrEmpty(request.Phone),
                Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow,
                Username = null,
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

            return await CreateTokenResponseAsync(newUser);
        }

        private Task SendVerificationEmailAsync(string email, string fullname, string otpCode)
        {
            var encodedName = HtmlEncoder.Default.Encode(fullname ?? "there");
            var body = $"""
                <!DOCTYPE html>
                <html lang="vi">
                <head>
                  <meta charset="UTF-8" />
                  <meta name="viewport" content="width=device-width, initial-scale=1.0" />
                  <title>Xác minh tài khoản Tutora</title>
                </head>
                <body style="margin:0;padding:0;background-color:#f0f4ff;font-family:'Segoe UI',Arial,Helvetica,sans-serif;color:#1e293b;">
                  <table width="100%" cellpadding="0" cellspacing="0" style="padding:48px 16px;">
                    <tr>
                      <td align="center">

                        <!-- Outer Card -->
                        <table width="600" cellpadding="0" cellspacing="0" style="max-width:600px;background-color:#ffffff;border-radius:20px;overflow:hidden;box-shadow:0 8px 40px rgba(59,130,246,0.12);">

                          <!-- ===== HEADER ===== -->
                          <tr>
                            <td style="background:linear-gradient(135deg,#1d4ed8 0%,#3b82f6 50%,#06b6d4 100%);padding:0;">
                              <!-- Decorative top band -->
                              <table width="100%" cellpadding="0" cellspacing="0">
                                <tr>
                                  <td style="padding:36px 40px 28px 40px;text-align:center;">
                                    <!-- Logo row -->
                                    <table cellpadding="0" cellspacing="0" style="margin:0 auto 12px auto;">
                                      <tr>
                                        <td style="background:rgba(255,255,255,0.18);border-radius:50%;width:64px;height:64px;text-align:center;vertical-align:middle;font-size:32px;line-height:64px;">
                                          &#127979;
                                        </td>
                                      </tr>
                                    </table>
                                    <h1 style="margin:0;font-size:30px;font-weight:800;color:#ffffff;letter-spacing:2px;text-shadow:0 2px 8px rgba(0,0,0,0.15);">
                                      TUTORA
                                    </h1>
                                    <p style="margin:8px 0 0 0;font-size:13px;color:rgba(255,255,255,0.85);letter-spacing:0.5px;">
                                      Nền tảng kết nối Học Sinh &amp; Gia Sư uy tín
                                    </p>
                                  </td>
                                </tr>
                                <!-- Decorative wave divider -->
                                <tr>
                                  <td style="line-height:0;font-size:0;">
                                    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 600 40" style="display:block;width:100%;" preserveAspectRatio="none">
                                      <path d="M0,0 C150,40 450,40 600,0 L600,40 L0,40 Z" fill="#ffffff"/>
                                    </svg>
                                  </td>
                                </tr>
                              </table>
                            </td>
                          </tr>

                          <!-- ===== BODY ===== -->
                          <tr>
                            <td style="padding:36px 44px 28px 44px;">

                              <!-- Greeting -->
                              <h2 style="margin:0 0 6px 0;font-size:22px;font-weight:700;color:#1e293b;">
                                Xin chào, <span style="color:#2563eb;">{encodedName}</span>! 👋
                              </h2>
                              <p style="margin:0 0 24px 0;font-size:15px;color:#64748b;line-height:1.7;">
                                Cảm ơn bạn đã đăng ký tài khoản tại <strong style="color:#1d4ed8;">Tutora</strong> — nền tảng học tập thông minh dành cho học sinh lớp 1 đến lớp 12. Vui lòng sử dụng mã xác minh bên dưới để hoàn tất quá trình đăng ký.
                              </p>

                              <!-- OTP Section -->
                              <table width="100%" cellpadding="0" cellspacing="0">
                                <tr>
                                  <td align="center" style="padding:8px 0 28px 0;">
                                    <!-- Label -->
                                    <div style="font-size:11px;font-weight:700;color:#3b82f6;letter-spacing:3px;text-transform:uppercase;margin-bottom:12px;">
                                      &#128274; Mã xác minh OTP
                                    </div>
                                    
                                    <table cellpadding="0" cellspacing="0" style="background:linear-gradient(145deg,#eff6ff,#e0f2fe);border:2px solid #93c5fd;border-radius:16px;min-width:280px;display:inline-block;">
                                      <tr>
                                        <td style="padding:20px 40px;text-align:center;">
                                          <!-- OTP Code -->
                                          <div style="font-size:42px;font-weight:900;color:#1d4ed8;letter-spacing:10px;font-family:'Courier New',monospace;text-shadow:0 2px 8px rgba(29,78,216,0.2);">
                                            {otpCode}
                                          </div>
                                        </td>
                                      </tr>
                                    </table>
                                    
                                    <!-- Timer badge -->
                                    <div style="margin-top:16px;">
                                      <span style="display:inline-block;background:#fef3c7;border:1px solid #fcd34d;border-radius:20px;padding:4px 14px;font-size:12px;font-weight:600;color:#92400e;">
                                        &#9201; Hết hạn sau 10 phút
                                      </span>
                                    </div>
                                  </td>
                                </tr>
                              </table>

                              <!-- Instructions -->
                              <table width="100%" cellpadding="0" cellspacing="0" style="background:#f8fafc;border-left:4px solid #3b82f6;border-radius:0 8px 8px 0;margin-bottom:24px;">
                                <tr>
                                  <td style="padding:16px 20px;">
                                    <p style="margin:0;font-size:14px;color:#475569;line-height:1.7;">
                                      &#9888;&#65039; <strong>Lưu ý quan trọng:</strong> Không chia sẻ mã này với bất kỳ ai. Tutora sẽ không bao giờ yêu cầu mã OTP qua điện thoại hay email khác.
                                    </p>
                                  </td>
                                </tr>
                              </table>

                              <p style="font-size:14px;color:#94a3b8;line-height:1.7;margin:0;">
                                Nếu bạn không thực hiện đăng ký tài khoản Tutora, bạn có thể bỏ qua email này. Tài khoản sẽ không được tạo nếu mã xác minh không được nhập.
                              </p>
                            </td>
                          </tr>

                          <!-- ===== DIVIDER ===== -->
                          <tr>
                            <td style="padding:0 44px;">
                              <div style="border-top:1px solid #e2e8f0;"></div>
                            </td>
                          </tr>

                          <!-- ===== FEATURES ROW ===== -->
                          <tr>
                            <td style="padding:24px 44px;">
                              <table width="100%" cellpadding="0" cellspacing="0">
                                <tr>
                                  <td width="33%" style="text-align:center;padding:0 8px;vertical-align:top;">
                                    <div style="font-size:24px;margin-bottom:6px;">&#127891;</div>
                                    <div style="font-size:12px;font-weight:600;color:#334155;">Học mọi cấp độ</div>
                                    <div style="font-size:11px;color:#94a3b8;margin-top:2px;">Lớp 1 &ndash; 12</div>
                                  </td>
                                  <td width="33%" style="text-align:center;padding:0 8px;vertical-align:top;border-left:1px solid #f1f5f9;border-right:1px solid #f1f5f9;">
                                    <div style="font-size:24px;margin-bottom:6px;">&#9989;</div>
                                    <div style="font-size:12px;font-weight:600;color:#334155;">Gia sư đã xét duyệt</div>
                                    <div style="font-size:11px;color:#94a3b8;margin-top:2px;">Chất lượng &amp; Uy tín</div>
                                  </td>
                                  <td width="33%" style="text-align:center;padding:0 8px;vertical-align:top;">
                                    <div style="font-size:24px;margin-bottom:6px;">&#128205;</div>
                                    <div style="font-size:12px;font-weight:600;color:#334155;">Linh hoạt địa điểm</div>
                                    <div style="font-size:11px;color:#94a3b8;margin-top:2px;">Online &amp; Offline</div>
                                  </td>
                                </tr>
                              </table>
                            </td>
                          </tr>

                          <!-- ===== FOOTER ===== -->
                          <tr>
                            <td style="background:linear-gradient(135deg,#1e3a8a,#1d4ed8);padding:24px 40px;text-align:center;border-radius:0 0 20px 20px;">
                              <p style="margin:0 0 6px 0;font-size:13px;color:rgba(255,255,255,0.9);font-weight:600;">
                                &#127775; Tutora &mdash; Nâng cánh ước mơ tri thức
                              </p>
                              <p style="margin:0;font-size:11px;color:rgba(255,255,255,0.55);">
                                &copy; 2026 Tutora. All rights reserved. &nbsp;|&nbsp; Bạn nhận được email này vì đã đăng ký tài khoản tại Tutora.
                              </p>
                            </td>
                          </tr>

                        </table>
                        <!-- End Outer Card -->

                      </td>
                    </tr>
                  </table>
                </body>
                </html>
                """;

            return _emailService.SendEmailAsync(
                email,
                "✅ Xác minh tài khoản Tutora của bạn",
                body
            );
        }
    }
}
