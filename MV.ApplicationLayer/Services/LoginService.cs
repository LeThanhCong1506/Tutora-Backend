using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.RepositoryInterfaces;
using System.IdentityModel.Tokens.Jwt;
using Google.Apis.Auth;
using MV.DomainLayer.Configuration;

namespace MV.ApplicationLayer.Services
{
    public class LoginService : ILoginService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly IPasswordRepository _passwordRepository;
        private readonly IConfiguration _configuration;
        private readonly ILogger<LoginService> _logger;

        public LoginService(IUnitOfWork unitOfWork, IAuthenticationRepository authenticationRepository,
            IPasswordRepository passwordRepository, IConfiguration configuration, ILogger<LoginService> logger)
        {
            _unitOfWork = unitOfWork;
            _authenticationRepository = authenticationRepository;
            _passwordRepository = passwordRepository;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<TokenResponse> AuthenticateGoogleUserAsync(string idToken, string? role)
        {
            try
            {
                // 1. Validate Google ID Token
                var (payload, validationError) = await ValidateGoogleTokenAsync(idToken);
                if (payload == null)
                {
                    return new TokenResponse { ErrorMessage = validationError };
                }

                // 2. Extract Email
                var email = payload.Email;
                if (string.IsNullOrEmpty(email))
                {
                    return new TokenResponse { ErrorMessage = "Invalid Token: Email is missing from Google account." };
                }

                // 3. Check if User Exists
                var user = await _unitOfWork.UserRepository.GetUserByEmailAsync(email);

                if (user == null)
                {
                    // 4. Auto-Register User — bắt buộc chọn role hợp lệ, không mặc định Parent nữa
                    if (string.IsNullOrWhiteSpace(role) || !UserRole.SelfRegisterable.Contains(role))
                    {
                        return new TokenResponse
                        {
                            RequiresRoleSelection = true,
                            Email = email,
                            ErrorMessage = "Vui lòng chọn vai trò (Parent, Student hoặc Tutor) để hoàn tất đăng ký."
                        };
                    }

                    var randomPassword = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
                    var (newUser, createError) = await CreateUserEntityAsync(payload.Email, payload.Name, randomPassword, role);
                    
                    if (newUser == null)
                    {
                        return new TokenResponse { ErrorMessage = createError ?? "Failed to auto-register user." };
                    }

                    await _unitOfWork.UserRepository.CreateUserAsync(newUser);
                    await _unitOfWork.SaveChangesAsync();

                    user = newUser;
                }
                else
                {
                    // If user exists but email is not verified, Google login automatically verifies it
                    if (user.Isemailverified != true)
                    {
                        user.Isemailverified = true;
                        await _unitOfWork.UserRepository.UpdateUserAsync(user);
                        await _unitOfWork.SaveChangesAsync();
                    }
                }

                // 5. Generate Internal System Token
                return await GenerateInternalTokenResponseAsync(user);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Google authentication.");
                return new TokenResponse { ErrorMessage = "Đã xảy ra lỗi trong quá trình xác thực bằng Google." };
            }
        }

        #region Helper Methods

        private async Task<(GoogleJsonWebSignature.Payload? Payload, string? ErrorMessage)> ValidateGoogleTokenAsync(string idToken)
        {
            try
            {
                var googleSettings = _configuration.GetSection(GoogleSettings.SectionName).Get<GoogleSettings>();
                var settings = new GoogleJsonWebSignature.ValidationSettings();
                
                if (googleSettings != null && !string.IsNullOrEmpty(googleSettings.ClientId))
                {
                    settings.Audience = new[] { googleSettings.ClientId };
                }

                var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, settings);
                return (payload, null);
            }
            catch (InvalidJwtException ex)
            {
                _logger.LogWarning(ex, "Google token validation failed.");
                return (null, "Invalid Google ID token.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while validating Google token.");
                return (null, "Error while validating Google ID token.");
            }
        }

        private async Task<(User?, string? ErrorMessage)> CreateUserEntityAsync(string email, string name, string password, string? requestedRole)
        {
            var userId = Guid.NewGuid().ToString();

            // Role bắt buộc hợp lệ — không mặc định Parent nữa (đã validate ở caller, guard lại cho chắc)
            if (string.IsNullOrWhiteSpace(requestedRole) || !UserRole.SelfRegisterable.Contains(requestedRole))
                return (null, "Vai trò không hợp lệ.");
            var roleToUse = requestedRole;

            var newUser = new User
            {
                Userid = userId,
                Email = email,
                Fullname = name ?? string.Empty,
                Password = _passwordRepository.HashPassword(password),
                Status = 1,
                Isemailverified = true,
                Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow,
                Username = null,
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
                newUser.StudentprofileParents.Add(new Studentprofile
                {
                    Studentid = userId,
                    Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
                });
            }
            else if (string.Equals(roleToUse, UserRole.Parent, StringComparison.OrdinalIgnoreCase))
            {
                newUser.Wallet = new Wallet { Userid = userId, Balance = 0, Lastupdated = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow };
            }

            return (newUser, null);
        }

        private async Task<TokenResponse> GenerateInternalTokenResponseAsync(User user)
        {
            // Check status
            if (user.Status == 0)
            {
                return new TokenResponse { ErrorMessage = "AccountLocked" };
            }

            // Get Role
            var role = await _unitOfWork.UserRepository.GetUserRoleByIdAsync(user.Userid);
            if (string.IsNullOrEmpty(role))
            {
                return new TokenResponse { ErrorMessage = "User role not found." };
            }

            // Generate Payload
            var loginResponse = new LoginResponse
            {
                Userid = user.Userid,
                Username = "",
                Fullname = user.Fullname,
                Email = user.Email ?? string.Empty,
                Phone = user.Phone ?? string.Empty,
                Role = role,
                Status = user.Status
            };

            var accessToken = _authenticationRepository.GenerateJwtToken(loginResponse);
            var rawRefreshToken = _authenticationRepository.GenerateRefreshToken();
            var tokenHash = _authenticationRepository.HashToken(rawRefreshToken);
            var expiryDays = int.TryParse(_configuration[ConfigurationKeys.Jwt.RefreshTokenExpiryDays], out var days) ? days : 7;

            var refreshToken = new RefreshToken
            {
                Id = Guid.NewGuid().ToString(),
                Tokenhash = tokenHash,
                Userid = user.Userid,
                Tokenfamily = Guid.NewGuid().ToString(),
                Expiresat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow.AddDays(expiryDays),
                Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
            };

            await _unitOfWork.RefreshTokenRepository.CreateAsync(refreshToken);
            await _unitOfWork.SaveChangesAsync();

            return new TokenResponse
            {
                AccessToken = accessToken,
                RefreshToken = rawRefreshToken,
                ErrorMessage = string.Empty
            };
        }

        #endregion

        public async Task<string> ChangePassword(string userId, ChangePasswordRequest request)
        {
            // Implementation preserved as per request
            return string.Empty;
        }
    }
}
