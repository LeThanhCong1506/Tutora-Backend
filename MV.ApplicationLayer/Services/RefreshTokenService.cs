using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.RepositoryInterfaces;
using System.Security.Claims;

namespace MV.ApplicationLayer.Services
{
    public class RefreshTokenService : IRefreshTokenService
    {
        // Reuse 1 refresh token vừa bị revoke trong khoảng này được coi là race vô hại (2 tab,
        // hoặc 2 nơi trên FE cùng tự refresh gần như đồng thời) chứ không phải bị đánh cắp —
        // xem RefreshAsync bước 3. Ngắn để không mở rộng đáng kể cửa sổ cho kẻ tấn công thật
        // (round-trip + xử lý của 1 race hợp lệ thường dưới vài giây).
        private static readonly TimeSpan ReuseGracePeriod = TimeSpan.FromSeconds(10);

        private readonly IUnitOfWork _unitOfWork;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly IConfiguration _configuration;
        private readonly ILogger<RefreshTokenService> _logger;

        public RefreshTokenService(
            IUnitOfWork unitOfWork,
            IAuthenticationRepository authenticationRepository,
            IConfiguration configuration,
            ILogger<RefreshTokenService> logger)
        {
            _unitOfWork = unitOfWork;
            _authenticationRepository = authenticationRepository;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<TokenResponse> RefreshAsync(string accessToken, string refreshToken)
        {
            try
            {
                // 1. Lấy claims từ access token đã hết hạn
                var principal = _authenticationRepository.GetPrincipalFromExpiredToken(accessToken);
                if (principal == null)
                {
                    return new TokenResponse { ErrorMessage = "Access token không hợp lệ." };
                }

                var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                {
                    return new TokenResponse { ErrorMessage = "Không tìm thấy userId trong token." };
                }

                // 2. Tìm refresh token trong DB theo hash
                var tokenHash = _authenticationRepository.HashToken(refreshToken);
                var storedToken = await _unitOfWork.RefreshTokenRepository.GetByTokenHashAsync(tokenHash);

                if (storedToken == null)
                {
                    return new TokenResponse { ErrorMessage = "Refresh token không tồn tại." };
                }

                // 3. Reuse detection: token đã bị revoke → có thể bị đánh cắp → revoke toàn bộ family.
                // Ngoại lệ: nếu vừa bị revoke trong ReuseGracePeriod, nhiều khả năng đây chỉ là 1 race
                // vô hại (VD 2 tab cùng 1 tài khoản, hoặc FE tự bắn 2 lần refresh gần như đồng thời) —
                // request thắng đã rotate xong, request thua tới sau dùng đúng token vừa bị thay thế.
                // Không thể trả lại y hệt token mà request thắng đã nhận (server chỉ lưu hash, không
                // lưu raw), nên thay vào đó cấp tiếp 1 cặp token mới cho request này luôn (coi như 1
                // lần rotate hợp lệ khác của cùng family), thay vì huỷ sạch mọi phiên đang hoạt động.
                if (storedToken.Revokedat.HasValue)
                {
                    var withinGracePeriod =
                        MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow - storedToken.Revokedat.Value <= ReuseGracePeriod;

                    if (!withinGracePeriod)
                    {
                        _logger.LogWarning("[RefreshToken] Reuse attack detected for family {Family}, userId {UserId}",
                            storedToken.Tokenfamily, userId);
                        await _unitOfWork.RefreshTokenRepository.RevokeAllByFamilyAsync(storedToken.Tokenfamily);
                        await _unitOfWork.SaveChangesAsync();
                        return new TokenResponse { ErrorMessage = "Refresh token đã bị thu hồi. Vui lòng đăng nhập lại." };
                    }

                    _logger.LogInformation(
                        "[RefreshToken] Benign reuse within grace period for family {Family}, userId {UserId} — treating as concurrent refresh, not an attack.",
                        storedToken.Tokenfamily, userId);
                }

                // 4. Kiểm tra hết hạn
                if (storedToken.Expiresat < MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow)
                {
                    return new TokenResponse { ErrorMessage = "Refresh token đã hết hạn. Vui lòng đăng nhập lại." };
                }

                // 5. Kiểm tra userId khớp
                if (storedToken.Userid != userId)
                {
                    return new TokenResponse { ErrorMessage = "Token không hợp lệ." };
                }

                // 6. Kiểm tra user còn active không
                var user = await _unitOfWork.UserRepository.GetUserByIdAsync(userId);
                if (user == null || user.Status == 0)
                {
                    return new TokenResponse { ErrorMessage = "Tài khoản không tồn tại hoặc đã bị khóa." };
                }

                // Cổng SĐT chỉ áp cho khách hàng — tài khoản nội bộ (Staff/Admin)
                // không có phone vẫn refresh bình thường; nếu không miễn, staff sẽ
                // bị revoke sạch token ngay lần silent-refresh đầu tiên. Tài khoản
                // con do phụ huynh tạo cũng miễn (xem SimpleAuthService cùng cổng
                // ở lúc đăng nhập) — nếu không, con đăng nhập được nhưng bị revoke
                // token và buộc đăng nhập lại ngay khi access token hết hạn.
                if (!UserRole.IsInternal(user.Primaryrole)
                    && !await IsParentManagedChildAccountAsync(userId)
                    && (string.IsNullOrWhiteSpace(user.Phone) || user.Isphoneverified != true))
                {
                    await _unitOfWork.RefreshTokenRepository.RevokeAllByUserIdAsync(userId);
                    await _unitOfWork.SaveChangesAsync();
                    return new TokenResponse
                    {
                        ErrorMessage = "Tài khoản phải có số điện thoại đã xác thực. Vui lòng đăng nhập lại và hoàn tất xác thực OTP.",
                        RequiresPhoneInput = string.IsNullOrWhiteSpace(user.Phone),
                        RequiresPhoneVerification = !string.IsNullOrWhiteSpace(user.Phone),
                        Phone = user.Phone
                    };
                }

                // 7. Lấy role
                var role = await _unitOfWork.UserRepository.GetUserRoleByIdAsync(userId);
                if (string.IsNullOrEmpty(role))
                {
                    return new TokenResponse { ErrorMessage = "Không tìm thấy role của user." };
                }

                // 8. Tạo access token mới
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
                var newAccessToken = _authenticationRepository.GenerateJwtToken(loginResponse);

                // 9. Tạo refresh token mới (rotation - cùng family)
                var newRawRefreshToken = _authenticationRepository.GenerateRefreshToken();
                var newTokenHash = _authenticationRepository.HashToken(newRawRefreshToken);
                var expiryDays = int.TryParse(_configuration[ConfigurationKeys.Jwt.RefreshTokenExpiryDays], out var days) ? days : 7;

                var newRefreshToken = new RefreshToken
                {
                    Id = Guid.NewGuid().ToString(),
                    Tokenhash = newTokenHash,
                    Userid = userId,
                    Tokenfamily = storedToken.Tokenfamily,
                    Expiresat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow.AddDays(expiryDays),
                    Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
                };

                // 10. Revoke token cũ, lưu token mới
                storedToken.Revokedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
                storedToken.Replacedbytokenhash = newTokenHash;
                await _unitOfWork.RefreshTokenRepository.CreateAsync(newRefreshToken);
                await _unitOfWork.SaveChangesAsync();

                return new TokenResponse
                {
                    AccessToken = newAccessToken,
                    RefreshToken = newRawRefreshToken,
                    ErrorMessage = string.Empty
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RefreshToken] Lỗi khi refresh token");
                return new TokenResponse { ErrorMessage = $"Lỗi: {ex.Message}" };
            }
        }

        public async Task RevokeAsync(string refreshToken)
        {
            try
            {
                var tokenHash = _authenticationRepository.HashToken(refreshToken);
                var storedToken = await _unitOfWork.RefreshTokenRepository.GetByTokenHashAsync(tokenHash);

                if (storedToken != null && !storedToken.Revokedat.HasValue)
                {
                    await _unitOfWork.RefreshTokenRepository.RevokeAllByFamilyAsync(storedToken.Tokenfamily);
                    await _unitOfWork.UserRepository.UpdateLastLoginAtAsync(storedToken.Userid, MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow);
                    await _unitOfWork.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RefreshToken] Lỗi khi revoke token");
            }
        }

        /// <summary>
        /// True nếu userId là tài khoản con do phụ huynh tạo (Studentprofile.Linkeduserid
        /// trỏ tới user này, Parentid khác null) — cùng định nghĩa với
        /// SimpleAuthService.IsParentManagedChildAccountAsync, giữ nguyên đây vì cổng SĐT
        /// được lặp lại độc lập ở từng service theo đúng convention hiện có của codebase.
        /// </summary>
        private async Task<bool> IsParentManagedChildAccountAsync(string userId)
        {
            var profile = await _unitOfWork.StudentRepository.FindByStudentOrLinkedUserAsync(userId);
            return profile?.Parentid != null;
        }
    }
}
