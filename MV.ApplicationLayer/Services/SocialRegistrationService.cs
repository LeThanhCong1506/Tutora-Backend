using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Helpers;
using MV.ApplicationLayer.Hubs;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.RepositoryInterfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.Services;

public class SocialRegistrationService : ISocialRegistrationService
{
    private const int SessionExpiryMinutes = 15;
    private const int OtpExpiryMinutes = 5;
    private const int MaxOtpAttempts = 5;

    // Cooldown chống spam gửi OTP qua Zalo ZNS — dùng ĐÚNG format key
    // "otp:phone:{phone}" mà SimpleAuthService dùng, để 1 số điện thoại bị
    // spam qua đăng ký social (Google/Zalo) và đăng ký thường (email/SĐT)
    // đều cộng dồn vào cùng 1 cooldown, không lách được bằng cách đổi đường đăng ký.
    private const int OtpResendCooldownSeconds = 60;

    // Giới hạn tổng số lần gửi OTP/24h cho CÙNG 1 số điện thoại — dùng chung key với
    // SimpleAuthService nên cộng dồn chung giữa 2 đường đăng ký (social + thường).
    private const int MaxOtpSendsPerDay = 5;
    private static readonly Regex PhoneRegex = new(
        @"^\+?\d{9,15}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordRepository _passwordRepository;
    private readonly IAuthenticationRepository _authenticationRepository;
    private readonly IOtpSender _otpSender;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SocialRegistrationService> _logger;
    private readonly IDistributedCache _cache;
    private readonly IHubContext<NotificationHub> _hubContext;

    public SocialRegistrationService(
        IUnitOfWork unitOfWork,
        IPasswordRepository passwordRepository,
        IAuthenticationRepository authenticationRepository,
        IOtpSender otpSender,
        IConfiguration configuration,
        ILogger<SocialRegistrationService> logger,
        IDistributedCache cache,
        IHubContext<NotificationHub> hubContext)
    {
        _unitOfWork = unitOfWork;
        _passwordRepository = passwordRepository;
        _authenticationRepository = authenticationRepository;
        _otpSender = otpSender;
        _configuration = configuration;
        _logger = logger;
        _cache = cache;
        _hubContext = hubContext;
    }

    public async Task<TokenResponse> BeginAsync(
        string provider,
        string providerUserId,
        string? email,
        string? fullName,
        string? avatarUrl)
    {
        if (!IsSupportedProvider(provider))
            return new TokenResponse { ErrorMessage = "Nhà cung cấp đăng nhập không hợp lệ." };

        var user = provider == SocialAuthProvider.Google
            ? await _unitOfWork.UserRepository.GetUserByEmailAsync(email ?? string.Empty)
            : await _unitOfWork.UserRepository.GetUserByZaloIdAsync(providerUserId);

        if (user != null)
        {
            if (user.Status == 0)
                return new TokenResponse { ErrorMessage = "Tài khoản đã bị khóa." };

            if (string.IsNullOrWhiteSpace(user.Primaryrole))
                return new TokenResponse { ErrorMessage = "User role not found." };

            if (!string.IsNullOrWhiteSpace(user.Phone) && user.Isphoneverified == true)
            {
                if (provider == SocialAuthProvider.Google && user.Isemailverified != true)
                    user.Isemailverified = true;

                user.Lastloginat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
                await _unitOfWork.UserRepository.UpdateUserAsync(user);
                await _unitOfWork.SaveChangesAsync();
                return await CreateTokenResponseAsync(user);
            }
        }

        var sessionToken = GenerateSessionToken();
        var session = new SocialRegistrationSession
        {
            Provider = provider,
            ProviderUserId = providerUserId,
            ExistingUserId = user?.Userid,
            Email = email,
            FullName = fullName,
            AvatarUrl = avatarUrl,
            Role = user?.Primaryrole,
            Phone = user?.Phone,
            ExpiresAtUtc = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow.AddMinutes(SessionExpiryMinutes)
        };

        await SaveSessionAsync(sessionToken, session);

        return new TokenResponse
        {
            RequiresRoleSelection = user == null && string.IsNullOrWhiteSpace(session.Role),
            RequiresPhoneInput = true,
            SocialRegistrationToken = sessionToken,
            Email = email,
            Phone = user?.Phone,
            ErrorMessage = string.Empty
        };
    }

    public async Task<TokenResponse> CompleteRegistrationAsync(CompleteSocialRegistrationRequest request)
    {
        var session = await GetValidSessionAsync(request.SocialRegistrationToken);
        if (session == null)
            return ExpiredSessionResponse();

        var phone = request.Phone.Trim();
        if (!PhoneRegex.IsMatch(phone))
        {
            return new TokenResponse
            {
                RequiresPhoneInput = true,
                SocialRegistrationToken = request.SocialRegistrationToken,
                Phone = phone,
                ErrorMessage = "Số điện thoại phải gồm 9-15 chữ số và có thể bắt đầu bằng dấu +."
            };
        }

        if (session.ExistingUserId == null)
        {
            var role = NormalizeRole(request.Role) ?? session.Role;
            if (string.IsNullOrWhiteSpace(role) || !UserRole.SelfRegisterable.Contains(role))
            {
                return new TokenResponse
                {
                    RequiresRoleSelection = true,
                    RequiresPhoneInput = true,
                    SocialRegistrationToken = request.SocialRegistrationToken,
                    ErrorMessage = "Vui lòng chọn vai trò Parent, Student hoặc Tutor."
                };
            }

            session.Role = role;
        }

        var phoneOwner = await _unitOfWork.UserRepository.GetUserByPhoneAsync(phone);
        if (phoneOwner != null && phoneOwner.Userid != session.ExistingUserId)
        {
            return new TokenResponse
            {
                RequiresPhoneInput = true,
                SocialRegistrationToken = request.SocialRegistrationToken,
                Phone = phone,
                ErrorMessage = "Số điện thoại đã được sử dụng."
            };
        }

        var blockReason = await GetOtpBlockReasonAsync(phone);
        if (blockReason != null)
        {
            return new TokenResponse
            {
                RequiresPhoneVerification = true,
                SocialRegistrationToken = request.SocialRegistrationToken,
                Phone = phone,
                ErrorMessage = blockReason
            };
        }

        session.Phone = phone;
        session.OtpCode = GenerateOtpCode();
        session.OtpAttempts = 0;
        session.OtpExpiresAtUtc = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow.AddMinutes(OtpExpiryMinutes);
        session.ExpiresAtUtc = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow.AddMinutes(SessionExpiryMinutes);

        await SaveSessionAsync(request.SocialRegistrationToken, session);
        await _otpSender.SendOtpAsync(phone, session.OtpCode);
        await MarkOtpSentAsync(phone);

        return new TokenResponse
        {
            RequiresPhoneVerification = true,
            SocialRegistrationToken = request.SocialRegistrationToken,
            Phone = phone,
            ErrorMessage = string.Empty
        };
    }

    public async Task<TokenResponse> VerifyPhoneOtpAsync(VerifySocialPhoneOtpRequest request)
    {
        var session = await GetValidSessionAsync(request.SocialRegistrationToken);
        if (session == null)
            return ExpiredSessionResponse();

        if (string.IsNullOrWhiteSpace(session.Phone) ||
            string.IsNullOrWhiteSpace(session.OtpCode) ||
            session.OtpExpiresAtUtc == null ||
            session.OtpExpiresAtUtc <= MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow)
        {
            return new TokenResponse
            {
                RequiresPhoneVerification = true,
                SocialRegistrationToken = request.SocialRegistrationToken,
                Phone = session.Phone,
                ErrorMessage = "OTP đã hết hạn. Vui lòng gửi lại mã mới."
            };
        }

        if (session.OtpAttempts >= MaxOtpAttempts)
        {
            return new TokenResponse
            {
                RequiresPhoneVerification = true,
                SocialRegistrationToken = request.SocialRegistrationToken,
                Phone = session.Phone,
                ErrorMessage = "Quá nhiều lần nhập OTP không hợp lệ. Vui lòng gửi lại mã mới."
            };
        }

        if (!string.Equals(session.OtpCode, request.Otp.Trim(), StringComparison.Ordinal))
        {
            session.OtpAttempts++;
            await SaveSessionAsync(request.SocialRegistrationToken, session);
            return new TokenResponse
            {
                RequiresPhoneVerification = true,
                SocialRegistrationToken = request.SocialRegistrationToken,
                Phone = session.Phone,
                ErrorMessage = "Mã OTP không hợp lệ."
            };
        }

        try
        {
            var phoneOwner = await _unitOfWork.UserRepository.GetUserByPhoneAsync(session.Phone);
            if (phoneOwner != null && phoneOwner.Userid != session.ExistingUserId)
            {
                return new TokenResponse
                {
                    RequiresPhoneInput = true,
                    SocialRegistrationToken = request.SocialRegistrationToken,
                    Phone = session.Phone,
                    ErrorMessage = "Số điện thoại đã được sử dụng."
                };
            }

            User user;
            if (session.ExistingUserId != null)
            {
                user = await _unitOfWork.UserRepository.GetUserByIdAsync(session.ExistingUserId)
                    ?? throw new InvalidOperationException("Không tìm thấy tài khoản social.");

                user.Phone = session.Phone;
                user.Isphoneverified = true;
                if (session.Provider == SocialAuthProvider.Google)
                    user.Isemailverified = true;

                await _unitOfWork.UserRepository.UpdateUserAsync(user);
            }
            else
            {
                user = await CreateUserAsync(session);
            }

            user.Lastloginat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
            await _unitOfWork.SaveChangesAsync();
            await _cache.RemoveAsync(SessionKey(request.SocialRegistrationToken));

            return await CreateTokenResponseAsync(user);
        }
        catch (DbUpdateException ex) when (IsUniqueUserConflict(ex))
        {
            _logger.LogWarning(ex, "Duplicate user data while completing social registration");
            return new TokenResponse { ErrorMessage = "Email, số điện thoại hoặc tài khoản social đã được sử dụng." };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while verifying social phone OTP");
            return new TokenResponse { ErrorMessage = "Không thể hoàn tất đăng ký social." };
        }
    }

    public async Task<TokenResponse> ResendPhoneOtpAsync(ResendSocialPhoneOtpRequest request)
    {
        var session = await GetValidSessionAsync(request.SocialRegistrationToken);
        if (session == null)
            return ExpiredSessionResponse();

        if (string.IsNullOrWhiteSpace(session.Phone))
        {
            return new TokenResponse
            {
                RequiresPhoneInput = true,
                SocialRegistrationToken = request.SocialRegistrationToken,
                ErrorMessage = "Vui lòng nhập số điện thoại trước."
            };
        }

        var resendBlockReason = await GetOtpBlockReasonAsync(session.Phone);
        if (resendBlockReason != null)
        {
            return new TokenResponse
            {
                RequiresPhoneVerification = true,
                SocialRegistrationToken = request.SocialRegistrationToken,
                Phone = session.Phone,
                ErrorMessage = resendBlockReason
            };
        }

        session.OtpCode = GenerateOtpCode();
        session.OtpAttempts = 0;
        session.OtpExpiresAtUtc = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow.AddMinutes(OtpExpiryMinutes);
        session.ExpiresAtUtc = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow.AddMinutes(SessionExpiryMinutes);
        await SaveSessionAsync(request.SocialRegistrationToken, session);
        await _otpSender.SendOtpAsync(session.Phone, session.OtpCode);
        await MarkOtpSentAsync(session.Phone);

        return new TokenResponse
        {
            RequiresPhoneVerification = true,
            SocialRegistrationToken = request.SocialRegistrationToken,
            Phone = session.Phone,
            ErrorMessage = string.Empty
        };
    }

    private async Task<User> CreateUserAsync(SocialRegistrationSession session)
    {
        var role = session.Role!;
        var userId = Guid.NewGuid().ToString();
        var isGoogle = session.Provider == SocialAuthProvider.Google;
        var socialAlias = isGoogle ? null : GenerateSocialAlias();
        var email = isGoogle
            ? session.Email
            : $"{socialAlias}@tutora.invalid";

        var user = new User
        {
            Userid = userId,
            Email = email!,
            Phone = session.Phone,
            Password = _passwordRepository.HashPassword(
                Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")),
            Fullname = session.FullName ?? string.Empty,
            Avatarurl = session.AvatarUrl,
            Zalouserid = isGoogle ? null : session.ProviderUserId,
            Username = socialAlias,
            Status = 1,
            Isemailverified = isGoogle,
            Isphoneverified = true,
            Zabornotifyenabled = true,
            Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow,
            Primaryrole = role
        };

        if (string.Equals(role, UserRole.Tutor, StringComparison.OrdinalIgnoreCase))
        {
            user.Tutorprofile = new Tutorprofile
            {
                Tutorid = userId,
                Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow,
                Profilestatus = TutorProfileStatus.Draft
            };
        }
        else if (string.Equals(role, UserRole.Student, StringComparison.OrdinalIgnoreCase))
        {
            user.StudentprofileLinkedusers.Add(new Studentprofile
            {
                Studentid = userId,
                Linkeduserid = userId,
                Fullname = user.Fullname,
                Avatarurl = user.Avatarurl,
                Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
            });
        }
        else if (string.Equals(role, UserRole.Parent, StringComparison.OrdinalIgnoreCase))
        {
            user.Wallet = new Wallet
            {
                Userid = userId,
                Balance = 0,
                Lastupdated = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
            };
        }

        await _unitOfWork.UserRepository.CreateUserAsync(user);
        return user;
    }

    private async Task<TokenResponse> CreateTokenResponseAsync(User user)
    {
        if (string.IsNullOrWhiteSpace(user.Phone) || user.Isphoneverified != true)
        {
            return new TokenResponse
            {
                ErrorMessage = "Tài khoản phải có số điện thoại đã xác thực trước khi nhận token.",
                RequiresPhoneInput = string.IsNullOrWhiteSpace(user.Phone),
                RequiresPhoneVerification = !string.IsNullOrWhiteSpace(user.Phone),
                Phone = user.Phone
            };
        }

        var role = await _unitOfWork.UserRepository.GetUserRoleByIdAsync(user.Userid)
            ?? user.Primaryrole;
        if (string.IsNullOrWhiteSpace(role))
            return new TokenResponse { ErrorMessage = "User role not found." };

        var loginResponse = new LoginResponse
        {
            Userid = user.Userid,
            Username = user.Username ?? string.Empty,
            Fullname = user.Fullname,
            Email = user.Email ?? string.Empty,
            Phone = user.Phone ?? string.Empty,
            Role = role,
            Status = user.Status
        };

        var accessToken = _authenticationRepository.GenerateJwtToken(loginResponse);
        var rawRefreshToken = _authenticationRepository.GenerateRefreshToken();
        var tokenHash = _authenticationRepository.HashToken(rawRefreshToken);
        var expiryDays = int.TryParse(
            _configuration[ConfigurationKeys.Jwt.RefreshTokenExpiryDays],
            out var days)
            ? days
            : 7;

        var refreshTokenEntity = new RefreshToken
        {
            Id = Guid.NewGuid().ToString(),
            Tokenhash = tokenHash,
            Userid = user.Userid,
            Tokenfamily = Guid.NewGuid().ToString(),
            Expiresat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow.AddDays(expiryDays),
            Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
        };
        await _unitOfWork.RefreshTokenRepository.CreateAsync(refreshTokenEntity);
        await _unitOfWork.SaveChangesAsync();

        // Đăng nhập Google/Zalo luôn qua trình duyệt (OAuth redirect) — không có đường nào cho
        // app Flutter tới đây, nên đây luôn là 1 "phiên web". Chỉ theo dõi ĐÚNG 1 con trỏ phiên
        // web hiện tại của user trong Redis (WebSessionTracker) — không kiểm tra platform của
        // bất kỳ token nào khác, không liên quan gì tới mobile.
        var previousWebTokenId = await WebSessionTracker.GetCurrentAsync(_cache, user.Userid);
        if (!string.IsNullOrEmpty(previousWebTokenId) && previousWebTokenId != refreshTokenEntity.Id)
        {
            await _unitOfWork.RefreshTokenRepository.RevokeTokensAsync(new[] { previousWebTokenId });
            await _unitOfWork.SaveChangesAsync();
        }
        await WebSessionTracker.SetAsync(_cache, user.Userid, refreshTokenEntity.Id, TimeSpan.FromDays(expiryDays));

        try
        {
            await _hubContext.Clients.Group($"user:{user.Userid}").SendAsync("ForceLogout", new { reason = "new_login" });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not push ForceLogout for user {UserId}", user.Userid);
        }

        return new TokenResponse
        {
            AccessToken = accessToken,
            RefreshToken = rawRefreshToken,
            ErrorMessage = string.Empty
        };
    }

    private async Task<SocialRegistrationSession?> GetValidSessionAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var json = await _cache.GetStringAsync(SessionKey(token));
        if (string.IsNullOrWhiteSpace(json))
            return null;

        var session = JsonSerializer.Deserialize<SocialRegistrationSession>(json);
        return session?.ExpiresAtUtc > MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
            ? session
            : null;
    }

    private Task SaveSessionAsync(string token, SocialRegistrationSession session)
    {
        var ttl = session.ExpiresAtUtc - MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
        if (ttl <= TimeSpan.Zero)
            ttl = TimeSpan.FromSeconds(1);

        return _cache.SetStringAsync(
            SessionKey(token),
            JsonSerializer.Serialize(session),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl });
    }

    private static string GenerateSessionToken()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    private static string GenerateSocialAlias()
        => $"social_{Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant()}";

    private static string GenerateOtpCode()
        => RandomNumberGenerator.GetInt32(100000, 1000000).ToString();

    private static string SessionKey(string token) => $"social-auth:{token.Trim()}";

    private static string OtpCooldownKey(string phone) => $"otp:phone:{phone.Trim()}:cooldown";

    private async Task<int> GetOtpCooldownSecondsLeftAsync(string phone)
    {
        var raw = await _cache.GetStringAsync(OtpCooldownKey(phone));
        if (string.IsNullOrEmpty(raw)
            || !DateTime.TryParse(raw, null, System.Globalization.DateTimeStyles.RoundtripKind, out var sentAtUtc))
            return 0;

        var secondsLeft = OtpResendCooldownSeconds
            - (MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow - sentAtUtc).TotalSeconds;
        return secondsLeft > 0 ? (int)Math.Ceiling(secondsLeft) : 0;
    }

    private static string OtpDailyLimitKey(string phone) => $"otp:phone:{phone.Trim()}:daily";

    private sealed class OtpDailyLimitEntry
    {
        public int Count { get; set; }
        public DateTime WindowStartUtc { get; set; }
    }

    private async Task<OtpDailyLimitEntry> GetOtpDailyLimitAsync(string phone)
    {
        var raw = await _cache.GetStringAsync(OtpDailyLimitKey(phone));
        if (!string.IsNullOrEmpty(raw))
        {
            var entry = JsonSerializer.Deserialize<OtpDailyLimitEntry>(raw)!;
            if (MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow - entry.WindowStartUtc < TimeSpan.FromHours(24))
                return entry;
        }
        return new OtpDailyLimitEntry { Count = 0, WindowStartUtc = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow };
    }

    private Task SaveOtpDailyLimitAsync(string phone, OtpDailyLimitEntry entry)
    {
        var remaining = TimeSpan.FromHours(24) - (MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow - entry.WindowStartUtc);
        if (remaining <= TimeSpan.Zero) remaining = TimeSpan.FromSeconds(1);
        return _cache.SetStringAsync(OtpDailyLimitKey(phone), JsonSerializer.Serialize(entry),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = remaining });
    }

    /// <summary>Check cả cooldown 60s VÀ giới hạn tổng số/ngày. Trả về lý do bị chặn, null nếu được gửi.</summary>
    private async Task<string?> GetOtpBlockReasonAsync(string phone)
    {
        var cooldownLeft = await GetOtpCooldownSecondsLeftAsync(phone);
        if (cooldownLeft > 0)
            return $"Vui lòng đợi {cooldownLeft} giây trước khi gửi lại mã.";

        var dailyEntry = await GetOtpDailyLimitAsync(phone);
        if (dailyEntry.Count >= MaxOtpSendsPerDay)
            return $"Số điện thoại này đã đạt giới hạn {MaxOtpSendsPerDay} lần gửi OTP trong 24 giờ. Vui lòng thử lại sau.";

        return null;
    }

    private async Task MarkOtpSentAsync(string phone)
    {
        await _cache.SetStringAsync(
            OtpCooldownKey(phone),
            MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow.ToString("o"),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(OtpResendCooldownSeconds) });

        var dailyEntry = await GetOtpDailyLimitAsync(phone);
        dailyEntry.Count++;
        await SaveOtpDailyLimitAsync(phone, dailyEntry);
    }

    private static string? NormalizeRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
            return null;

        return UserRole.SelfRegisterable.FirstOrDefault(
            value => string.Equals(value, role.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSupportedProvider(string provider)
        => provider is SocialAuthProvider.Google or SocialAuthProvider.Zalo;

    private static TokenResponse ExpiredSessionResponse()
        => new()
        {
            ErrorMessage = "Phiên đăng ký social đã hết hạn. Vui lòng đăng nhập lại bằng Google hoặc Zalo."
        };

    private static bool IsUniqueUserConflict(DbUpdateException ex)
    {
        var message = $"{ex.Message} {ex.InnerException?.Message}";
        return message.Contains("users_phone_key", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("users_email_key", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("users_zalouserid_key", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("users_username_key", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class SocialRegistrationSession
    {
        public string Provider { get; set; } = string.Empty;
        public string ProviderUserId { get; set; } = string.Empty;
        public string? ExistingUserId { get; set; }
        public string? Email { get; set; }
        public string? FullName { get; set; }
        public string? AvatarUrl { get; set; }
        public string? Role { get; set; }
        public string? Phone { get; set; }
        public string? OtpCode { get; set; }
        public int OtpAttempts { get; set; }
        public DateTime? OtpExpiresAtUtc { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
    }
}
