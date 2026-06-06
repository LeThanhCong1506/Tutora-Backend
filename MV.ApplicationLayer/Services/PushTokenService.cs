using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.ApplicationLayer.Interfaces;

namespace MV.ApplicationLayer.Services
{
    public class PushTokenService : IPushTokenService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<PushTokenService> _logger;

        public PushTokenService(IUnitOfWork unitOfWork, ILogger<PushTokenService> logger)
        {
            _unitOfWork = unitOfWork;
            _logger = logger;
        }

        public async Task<bool> SavePushTokenAsync(string userId, string fcmToken)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(fcmToken))
                {
                    _logger.LogWarning("Invalid userId or fcmToken provided.");
                    return false;
                }

                var result = await _unitOfWork.UserRepository.UpdateFcmTokenAsync(userId, fcmToken);

                if (result)
                {
                    await _unitOfWork.SaveChangesAsync();
                    _logger.LogInformation("FCM token saved successfully for user {UserId}", userId);
                }
                else
                {
                    _logger.LogWarning("Failed to save FCM token for user {UserId}. User not found.", userId);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while saving FCM token for user {UserId}", userId);
                return false;
            }
        }

        public async Task<bool> DeletePushTokenAsync(string userId, string fcmToken)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(userId))
                {
                    _logger.LogWarning("Invalid userId provided.");
                    return false;
                }

                var result = await _unitOfWork.UserRepository.UpdateFcmTokenAsync(userId, string.Empty);

                if (result)
                {
                    await _unitOfWork.SaveChangesAsync();
                    _logger.LogInformation("FCM token removed successfully for user {UserId}", userId);
                }
                else
                {
                    _logger.LogWarning("Failed to remove FCM token for user {UserId}. User not found.", userId);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while removing FCM token for user {UserId}", userId);
                return false;
            }
        }
    }
}
