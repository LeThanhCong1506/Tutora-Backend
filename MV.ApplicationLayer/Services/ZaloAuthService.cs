using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.RepositoryInterfaces;
using System.Text.Json;

namespace MV.ApplicationLayer.Services
{
    public class ZaloAuthService : IZaloAuthService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ZaloAuthService> _logger;
        private readonly IHttpClientFactory _httpClientFactory;

        public ZaloAuthService(
            IUnitOfWork unitOfWork,
            IAuthenticationRepository authenticationRepository,
            IConfiguration configuration,
            ILogger<ZaloAuthService> logger,
            IHttpClientFactory httpClientFactory)
        {
            _unitOfWork = unitOfWork;
            _authenticationRepository = authenticationRepository;
            _configuration = configuration;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
        }

        public async Task<TokenResponse> LoginWithZaloAsync(ZaloLoginRequest request)
        {
            try
            {
                // 1. Verify Zalo access token via Zalo Graph API — lấy userId từ token
                var verifiedZaloId = await VerifyZaloTokenAsync(request.ZaloAccessToken, request.ZaloUserId);
                if (verifiedZaloId == null)
                {
                    return new TokenResponse { ErrorMessage = "Zalo token không hợp lệ." };
                }

                // 2. Find existing user by ZaloUserId (dùng verifiedZaloId, không phải request.ZaloUserId)
                var user = await _unitOfWork.UserRepository.GetUserByZaloIdAsync(verifiedZaloId);

                // 3. Auto-create user if not found
                if (user == null)
                {
                    user = await CreateZaloUserAsync(request, verifiedZaloId);
                    if (user == null)
                        return new TokenResponse { ErrorMessage = "Không thể tạo tài khoản." };
                }
                else if (user.Primaryrole == UserRole.Student)
                {
                    // Đảm bảo user Student có student profile (fix cho users tạo trước khi có auto-create)
                    var existingProfiles = await _unitOfWork.StudentRepository.GetByLinkedUserIdAsync(user.Userid);
                    if (!existingProfiles.Any())
                    {
                        var studentProfile = new Studentprofile
                        {
                            Studentid = await _unitOfWork.StudentRepository.GenerateUniqueStudentIdAsync(),
                            Linkeduserid = user.Userid,
                            Fullname = user.Fullname,
                            Avatarurl = user.Avatarurl,
                            Createdat = MV.DomainLayer.Helpers.VietnamTimeHelper.Now,
                        };
                        await _unitOfWork.StudentRepository.CreateAsync(studentProfile);
                        await _unitOfWork.SaveChangesAsync();
                        _logger.LogInformation("Created missing student profile for existing user {UserId}", user.Userid);
                    }
                }

                if (user.Status == 0)
                    return new TokenResponse { ErrorMessage = "Tài khoản đã bị khóa." };

                // 4. Get role
                var role = await _unitOfWork.UserRepository.GetUserRoleByIdAsync(user.Userid);
                if (string.IsNullOrEmpty(role)) role = user.Primaryrole ?? UserRole.Parent;

                // 5. Issue JWT
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi Zalo login cho userId={ZaloUserId}", request.ZaloUserId);
                return new TokenResponse { ErrorMessage = $"Lỗi: {ex.Message}" };
            }
        }

        /// <summary>
        /// Verify Zalo access token via Graph API. Returns Zalo user ID or null if invalid.
        /// </summary>
        private async Task<string?> VerifyZaloTokenAsync(string zaloAccessToken, string? fallbackZaloId = null)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                var appSecret = _configuration[ConfigurationKeys.ZaloOA.SecretKey]
                    ?? _configuration[ConfigurationKeys.ZaloOA.AppSecretKey]
                    ?? string.Empty;
                var url = $"https://graph.zalo.me/v2.0/me?access_token={Uri.EscapeDataString(zaloAccessToken)}&fields=id,name,picture";
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (!string.IsNullOrEmpty(appSecret))
                    request.Headers.Add("secret_key", appSecret);
                var response = await client.SendAsync(request);

                var body = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("Zalo Graph API response: {Status} {Body}", response.StatusCode, body);

                if (!response.IsSuccessStatusCode) return null;

                var json = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(body);

                // Check for API-level error (HTTP 200 nhưng body có error field)
                if (json.TryGetProperty("error", out var errProp) && errProp.GetInt32() != 0)
                {
                    var errCode = errProp.GetInt32();
                    var errMsg = json.TryGetProperty("message", out var msgProp) ? msgProp.GetString() : DisplayValues.Unknown;
                    _logger.LogWarning("Zalo Graph API error {Code}: {Message}", errCode, errMsg);

                    // -501: IP ngoài Việt Nam — dùng fallback ID từ client (lấy qua Zalo SDK)
                    if (errCode == -501 && !string.IsNullOrEmpty(fallbackZaloId))
                    {
                        _logger.LogInformation("Using fallback ZaloUserId due to -501 IP restriction");
                        return fallbackZaloId;
                    }
                    return null;
                }

                return json.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Zalo token verification failed");
                return null;
            }
        }

        private async Task<User?> CreateZaloUserAsync(ZaloLoginRequest request, string verifiedZaloId)
        {
            var userId = Guid.NewGuid().ToString();
            var newUser = new User
            {
                Userid = userId,
                Fullname = request.Name ?? "Người dùng Zalo",
                Avatarurl = request.Avatar,
                Zalouserid = verifiedZaloId,
                Email = $"zalo_{verifiedZaloId}@tutora.vn",
                Username = $"zalo_{verifiedZaloId}",
                Zabornotifyenabled = true,
                Status = 1,
                Primaryrole = request.Role is UserRole.Parent or UserRole.Student or UserRole.Tutor ? request.Role : UserRole.Parent,
                Createdat = MV.DomainLayer.Helpers.VietnamTimeHelper.Now,
                // Random password — user will only login via Zalo
                Password = Guid.NewGuid().ToString("N"),
                Wallet = new Wallet
                {
                    Userid = userId,
                    Balance = 0,
                    Lastupdated = MV.DomainLayer.Helpers.VietnamTimeHelper.Now
                }
            };

            await _unitOfWork.UserRepository.CreateUserAsync(newUser);

            // Nếu role Student, tạo luôn student profile
            if (newUser.Primaryrole == UserRole.Student)
            {
                var studentProfile = new Studentprofile
                {
                    Studentid = await _unitOfWork.StudentRepository.GenerateUniqueStudentIdAsync(),
                    Linkeduserid = userId,
                    Fullname = newUser.Fullname,
                    Avatarurl = newUser.Avatarurl,
                    Createdat = MV.DomainLayer.Helpers.VietnamTimeHelper.Now,
                };
                await _unitOfWork.StudentRepository.CreateAsync(studentProfile);
            }

            await _unitOfWork.SaveChangesAsync();
            return newUser;
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
                Expiresat = MV.DomainLayer.Helpers.VietnamTimeHelper.Now.AddDays(expiryDays),
                Createdat = MV.DomainLayer.Helpers.VietnamTimeHelper.Now
            };

            await _unitOfWork.RefreshTokenRepository.CreateAsync(refreshToken);
            await _unitOfWork.SaveChangesAsync();
            return rawToken;
        }
    }
}
