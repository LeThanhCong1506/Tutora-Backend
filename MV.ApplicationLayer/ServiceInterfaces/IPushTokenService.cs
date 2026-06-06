namespace MV.ApplicationLayer.ServiceInterfaces
{
    public interface IPushTokenService
    {
        /// <summary>
        /// Register or refresh an FCM device token for push notification delivery.
        /// </summary>
        Task<bool> SavePushTokenAsync(string userId, string fcmToken);

        /// <summary>
        /// Remove an FCM device token (e.g. on logout or token rotation).
        /// </summary>
        Task<bool> DeletePushTokenAsync(string userId, string fcmToken);
    }
}
