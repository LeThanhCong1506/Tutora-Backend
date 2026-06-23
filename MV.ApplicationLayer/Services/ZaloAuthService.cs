using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.RepositoryInterfaces;
using System.Net.Http;
using System.Text.Json;

namespace MV.ApplicationLayer.Services
{
    public class ZaloAuthService : IZaloAuthService
    {
        private const string TokenEndpoint = "https://oauth.zaloapp.com/v4/access_token";
        private const string GraphMeEndpoint = "https://graph.zalo.me/v2.0/me";

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

        public async Task<TokenResponse> LoginWithZaloCodeAsync(ZaloWebLoginRequest request)
        {
            try
            {
                // 1. Đổi authorization code lấy Zalo access token (PKCE)
                var accessToken = await ExchangeCodeForTokenAsync(request.Code, request.CodeVerifier);
                if (string.IsNullOrEmpty(accessToken))
                {
                    return new TokenResponse { ErrorMessage = "Không đổi được mã đăng nhập Zalo (code không hợp lệ hoặc đã hết hạn)." };
                }

                // 2. Lấy profile từ Zalo Graph API
                var profile = await GetZaloProfileAsync(accessToken);
                if (profile == null || string.IsNullOrEmpty(profile.ZaloId))
                {
                    return new TokenResponse { ErrorMessage = "Không lấy được thông tin tài khoản Zalo." };
                }

                // 3. Tìm user theo ZaloUserId
                var user = await _unitOfWork.UserRepository.GetUserByZaloIdAsync(profile.ZaloId);

                // 4. Auto-register nếu chưa có — bắt buộc role hợp lệ, không mặc định Parent
                if (user == null)
                {
                    if (string.IsNullOrWhiteSpace(request.Role) || !UserRole.SelfRegisterable.Contains(request.Role))
                    {
                        return new TokenResponse
                        {
                            RequiresRoleSelection = true,
                            Email = null,
                            ErrorMessage = "Vui lòng chọn vai trò (Parent, Student hoặc Tutor) để hoàn tất đăng ký."
                        };
                    }

                    user = await CreateZaloUserAsync(profile, request.Role);
                    if (user == null)
                        return new TokenResponse { ErrorMessage = "Không thể tạo tài khoản." };
                }

                if (user.Status == 0)
                    return new TokenResponse { ErrorMessage = "Tài khoản đã bị khóa." };

                // 5. Get role
                var role = await _unitOfWork.UserRepository.GetUserRoleByIdAsync(user.Userid);
                if (string.IsNullOrEmpty(role)) role = user.Primaryrole ?? UserRole.Parent;

                // 6. Issue JWT + refresh token
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

                var accessJwt = _authenticationRepository.GenerateJwtToken(loginResponse);
                var rawRefreshToken = await CreateRefreshTokenAsync(user.Userid);

                return new TokenResponse
                {
                    AccessToken = accessJwt,
                    RefreshToken = rawRefreshToken,
                    ErrorMessage = string.Empty
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi Zalo web login");
                return new TokenResponse { ErrorMessage = $"Lỗi: {ex.Message}" };
            }
        }

        /// <summary>
        /// Đổi authorization code → access token tại oauth.zaloapp.com/v4/access_token.
        /// App secret nằm ở backend (header secret_key), không bao giờ lộ ra FE.
        /// </summary>
        private async Task<string?> ExchangeCodeForTokenAsync(string code, string codeVerifier)
        {
            var appId = _configuration[ConfigurationKeys.ZaloOA.AppId] ?? string.Empty;
            var appSecret = _configuration[ConfigurationKeys.ZaloOA.SecretKey]
                ?? _configuration[ConfigurationKeys.ZaloOA.AppSecretKey]
                ?? string.Empty;

            if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(appSecret))
            {
                _logger.LogError("Thiếu cấu hình ZaloOA:AppId hoặc ZaloOA:SecretKey.");
                return null;
            }

            var client = _httpClientFactory.CreateClient();

            // Zalo v4 token exchange: application/x-www-form-urlencoded, app_secret qua header secret_key
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = code,
                ["app_id"] = appId,
                ["grant_type"] = "authorization_code",
                ["code_verifier"] = codeVerifier
            });

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint) { Content = form };
            httpRequest.Headers.Add("secret_key", appSecret);

            var response = await client.SendAsync(httpRequest);
            var body = await response.Content.ReadAsStringAsync();
            _logger.LogInformation("Zalo token exchange response: {Status} {Body}", response.StatusCode, body);

            if (!response.IsSuccessStatusCode) return null;

            var json = JsonSerializer.Deserialize<JsonElement>(body);

            // Zalo trả error != 0 trong body khi thất bại (kèm HTTP 200)
            if (json.TryGetProperty("error", out var errProp) && errProp.ValueKind == JsonValueKind.Number && errProp.GetInt32() != 0)
            {
                var errMsg = json.TryGetProperty("error_description", out var d) ? d.GetString()
                           : json.TryGetProperty("message", out var m) ? m.GetString()
                           : DisplayValues.Unknown;
                _logger.LogWarning("Zalo token exchange error {Code}: {Message}", errProp.GetInt32(), errMsg);
                return null;
            }

            return json.TryGetProperty("access_token", out var tokenProp) ? tokenProp.GetString() : null;
        }

        /// <summary>
        /// Lấy profile người dùng Zalo qua Graph API. Trả về null nếu token sai.
        /// </summary>
        private async Task<ZaloProfile?> GetZaloProfileAsync(string accessToken)
        {
            var client = _httpClientFactory.CreateClient();
            var appSecret = _configuration[ConfigurationKeys.ZaloOA.SecretKey]
                ?? _configuration[ConfigurationKeys.ZaloOA.AppSecretKey]
                ?? string.Empty;

            var url = $"{GraphMeEndpoint}?access_token={Uri.EscapeDataString(accessToken)}&fields=id,name,picture";
            var httpRequest = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(appSecret))
                httpRequest.Headers.Add("secret_key", appSecret);

            var response = await client.SendAsync(httpRequest);
            var body = await response.Content.ReadAsStringAsync();
            _logger.LogInformation("Zalo Graph API response: {Status} {Body}", response.StatusCode, body);

            if (!response.IsSuccessStatusCode) return null;

            var json = JsonSerializer.Deserialize<JsonElement>(body);

            if (json.TryGetProperty("error", out var errProp) && errProp.ValueKind == JsonValueKind.Number && errProp.GetInt32() != 0)
            {
                var errMsg = json.TryGetProperty("message", out var m) ? m.GetString() : DisplayValues.Unknown;
                _logger.LogWarning("Zalo Graph API error {Code}: {Message}", errProp.GetInt32(), errMsg);
                return null;
            }

            var zaloId = json.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
            if (string.IsNullOrEmpty(zaloId)) return null;

            var name = json.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;

            string? avatar = null;
            if (json.TryGetProperty("picture", out var picProp) && picProp.ValueKind == JsonValueKind.Object
                && picProp.TryGetProperty("data", out var picData) && picData.ValueKind == JsonValueKind.Object
                && picData.TryGetProperty("url", out var picUrl) && picUrl.ValueKind == JsonValueKind.String)
            {
                avatar = picUrl.GetString();
            }

            return new ZaloProfile { ZaloId = zaloId, Name = name, Avatar = avatar };
        }

        private async Task<User?> CreateZaloUserAsync(ZaloProfile profile, string role)
        {
            // Role đã được validate ở caller, guard lại cho chắc
            if (string.IsNullOrWhiteSpace(role) || !UserRole.SelfRegisterable.Contains(role))
                return null;

            var userId = Guid.NewGuid().ToString();
            var newUser = new User
            {
                Userid = userId,
                Fullname = profile.Name ?? "Người dùng Zalo",
                Avatarurl = profile.Avatar,
                Zalouserid = profile.ZaloId,
                Email = $"zalo_{profile.ZaloId}@tutora.vn",
                Username = $"zalo_{profile.ZaloId}",
                Zabornotifyenabled = true,
                Status = 1,
                Primaryrole = role,
                Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow,
                // Random password — user chỉ đăng nhập qua Zalo
                Password = Guid.NewGuid().ToString("N"),
                Wallet = new Wallet
                {
                    Userid = userId,
                    Balance = 0,
                    Lastupdated = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
                }
            };

            await _unitOfWork.UserRepository.CreateUserAsync(newUser);

            // Role Student → tạo luôn student profile
            if (newUser.Primaryrole == UserRole.Student)
            {
                var studentProfile = new Studentprofile
                {
                    Studentid = await _unitOfWork.StudentRepository.GenerateUniqueStudentIdAsync(),
                    Linkeduserid = userId,
                    Fullname = newUser.Fullname,
                    Avatarurl = newUser.Avatarurl,
                    Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow,
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
                Expiresat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow.AddDays(expiryDays),
                Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
            };

            await _unitOfWork.RefreshTokenRepository.CreateAsync(refreshToken);
            await _unitOfWork.SaveChangesAsync();
            return rawToken;
        }

        private sealed class ZaloProfile
        {
            public string ZaloId { get; set; } = string.Empty;
            public string? Name { get; set; }
            public string? Avatar { get; set; }
        }
    }
}
