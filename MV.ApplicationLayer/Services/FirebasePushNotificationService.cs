using FirebaseAdmin.Messaging;
using Microsoft.Extensions.Logging;

namespace MV.ApplicationLayer.Services
{
    public interface IFirebasePushNotificationService
    {
        Task<bool> SendPushNotificationAsync(string fcmToken, string title, string body, Dictionary<string, string>? data = null);
    }

    public class FirebasePushNotificationService : IFirebasePushNotificationService
    {
        private readonly ILogger<FirebasePushNotificationService> _logger;

        public FirebasePushNotificationService(ILogger<FirebasePushNotificationService> logger)
        {
            _logger = logger;
        }

        public async Task<bool> SendPushNotificationAsync(string fcmToken, string title, string body, Dictionary<string, string>? data = null)
        {
            try
            {
                if (string.IsNullOrEmpty(fcmToken))
                {
                    _logger.LogWarning("FCM Token is null or empty. Cannot send push notification.");
                    return false;
                }

                var message = new Message()
                {
                    Notification = new Notification
                    {
                        Title = title,
                        Body = body
                    },
                    Data = data,
                    Token = fcmToken
                };

                var messaging = FirebaseMessaging.DefaultInstance;
                string response = await messaging.SendAsync(message);

                _logger.LogInformation("Push notification sent successfully: {Response}", response);
                return true;
            }
            catch (FirebaseMessagingException ex)
            {
                _logger.LogError(ex, "Firebase error when sending push notification: {ErrorCode}", ex.MessagingErrorCode);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending push notification");
                return false;
            }
        }
    }
}
