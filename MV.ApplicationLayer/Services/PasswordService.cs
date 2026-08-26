using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.RepositoryInterfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;

namespace MV.ApplicationLayer.Services
{
    public class PasswordService : IPasswordService
    {
        private readonly IUserRepository _userRepository;
        private readonly IAppDbContext _dbContext;
        private readonly IPasswordRepository _passwordRepository;
        private readonly IConfiguration _configuration;
        private readonly ILogger<PasswordService> _logger;

        public PasswordService(IUserRepository userRepository, IAppDbContext dbContext, IPasswordRepository passwordRepository,
            IConfiguration configuration, ILogger<PasswordService> logger)
        {
            _userRepository = userRepository;
            _dbContext = dbContext;
            _passwordRepository = passwordRepository;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<(bool Success, string? ErrorMessage)> ChangePasswordAsync(string userId, string oldPassword, string newPassword)
        {
            try
            {
                // 1. Get user from DB
                var user = await _userRepository.GetUserByIdAsync(userId);
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

                await _userRepository.UpdateUserAsync(user);
                await _dbContext.SaveChangesAsync();

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
