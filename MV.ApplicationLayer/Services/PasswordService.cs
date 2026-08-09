using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using MV.ApplicationLayer.Hubs;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.RepositoryInterfaces;
using System.IdentityModel.Tokens.Jwt;

namespace MV.ApplicationLayer.Services
{
    public class PasswordService : IPasswordService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IPasswordRepository _passwordRepository;
        private readonly IConfiguration _configuration;
        private readonly ILogger<PasswordService> _logger;
        private readonly IHubContext<NotificationHub> _hubContext;

        public PasswordService(IUnitOfWork unitOfWork, IPasswordRepository passwordRepository,
            IConfiguration configuration, ILogger<PasswordService> logger, IHubContext<NotificationHub> hubContext)
        {
            _unitOfWork = unitOfWork;
            _passwordRepository = passwordRepository;
            _configuration = configuration;
            _logger = logger;
            _hubContext = hubContext;
        }

        public async Task<(bool Success, string? ErrorMessage)> ChangePasswordAsync(string userId, string oldPassword, string newPassword)
        {
            try
            {
                // 1. Get user from DB
                var user = await _unitOfWork.UserRepository.GetUserByIdAsync(userId);
                if (user == null)
                {
                    return (false, ApiMessages.UserNotFoundWithPeriod);
                }

                // 2. Verify old password
                if (!_passwordRepository.VerifyPassword(oldPassword, user.Password))
                {
                    return (false, "Mật khẩu cũ không đúng.");
                }

                // 3. Validate new password (optional: add more rules)
                if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
                {
                    return (false, "Mật khẩu mới phải có ít nhất 6 ký tự.");
                }

                if (oldPassword == newPassword)
                {
                    return (false, "Mật khẩu mới không được trùng với mật khẩu cũ. Vui lòng chọn mật khẩu khác.");
                }

                // 4. Hash new password and update
                var hashedPassword = _passwordRepository.HashPassword(newPassword);
                user.Password = hashedPassword;

                await _unitOfWork.UserRepository.UpdateUserAsync(user);
                await _unitOfWork.SaveChangesAsync();

                // Đổi mật khẩu → thu hồi MỌI phiên (web + mobile), không loại trừ platform
                // nào — khác với luật 1-phiên-web ở đăng nhập, đổi mật khẩu là hành động an
                // ninh mạnh hơn, phải đăng nhập lại ở tất cả thiết bị.
                await _unitOfWork.RefreshTokenRepository.RevokeAllByUserIdAsync(userId);
                await _unitOfWork.SaveChangesAsync();
                try
                {
                    await _hubContext.Clients.Group($"user:{userId}").SendAsync(
                        "ForceLogout", new { reason = "password_changed" });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not push ForceLogout for user {UserId}", userId);
                }

                return (true, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error changing password for user {UserId}.", userId);
                return (false, "Có lỗi xảy ra khi đổi mật khẩu.");
            }
        }

    }
}
