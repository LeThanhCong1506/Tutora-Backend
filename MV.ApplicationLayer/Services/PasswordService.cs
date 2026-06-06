using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
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

        public PasswordService(IUnitOfWork unitOfWork, IPasswordRepository passwordRepository,
            IConfiguration configuration, ILogger<PasswordService> logger)
        {
            _unitOfWork = unitOfWork;
            _passwordRepository = passwordRepository;
            _configuration = configuration;
            _logger = logger;
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
                    return (false, "Old password is incorrect.");
                }

                // 3. Validate new password (optional: add more rules)
                if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
                {
                    return (false, "New password must be at least 6 characters.");
                }

                if (oldPassword == newPassword)
                {
                    return (false, "New password must be different from old password.");
                }

                // 4. Hash new password and update
                var hashedPassword = _passwordRepository.HashPassword(newPassword);
                user.Password = hashedPassword;

                await _unitOfWork.UserRepository.UpdateUserAsync(user);
                await _unitOfWork.SaveChangesAsync();

                return (true, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error changing password for user {UserId}.", userId);
                return (false, "An error occurred while changing password.");
            }
        }

    }
}
