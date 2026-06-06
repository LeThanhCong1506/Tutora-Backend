using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces
{
    public interface INotificationService
    {
        /// <summary>
        /// Persist and push a single in-app notification to a user.
        /// </summary>
        Task<StatusResponse> CreateNotificationAsync(NotificationRequest request);

        /// <summary>
        /// Persist and push a batch of in-app notifications in one call.
        /// </summary>
        Task<StatusResponse> CreateNotificationsAsync(IEnumerable<NotificationRequest> requests);

        /// <summary>
        /// Single notification by id.
        /// </summary>
        Task<NotificationResponse?> GetNotificationByIdAsync(int notificationId);

        /// <summary>
        /// All notifications for a user, newest first.
        /// </summary>
        Task<IEnumerable<NotificationResponse>> GetNotificationsByUserIdAsync(string userId);

        /// <summary>
        /// Only unread notifications for a user.
        /// </summary>
        Task<IEnumerable<NotificationResponse>> GetUnreadNotificationsByUserIdAsync(string userId);

        /// <summary>
        /// Count of unread notifications for a user (used for badge display).
        /// </summary>
        Task<int> GetUnreadCountByUserIdAsync(string userId);

        /// <summary>
        /// Count of unread notifications, with a breakdown grouped by non-null notification type.
        /// </summary>
        Task<UnreadCountResponse> GetUnreadCountResponseByUserIdAsync(string userId);

        /// <summary>
        /// All notifications across all users (admin view).
        /// </summary>
        Task<IEnumerable<NotificationResponse>> GetAllNotificationsAsync();

        /// <summary>
        /// Mark a single notification as read; requires the current user to own it.
        /// </summary>
        Task<StatusResponse> MarkAsReadAsync(int notificationId, string currentUserId);

        /// <summary>
        /// Mark all notifications for a user as read in one call.
        /// </summary>
        Task<StatusResponse> MarkAllAsReadAsync(string userId);

        /// <summary>
        /// Mark all unread notifications of one type as read for the current user.
        /// </summary>
        Task<StatusResponse> MarkAsReadByTypeAsync(string userId, string type);

        /// <summary>
        /// Delete a single notification; requires the current user to own it.
        /// </summary>
        Task<StatusResponse> DeleteNotificationAsync(int notificationId, string currentUserId);

        /// <summary>
        /// Delete all notifications for a user.
        /// </summary>
        Task<StatusResponse> DeleteAllNotificationsByUserIdAsync(string userId);

        /// <summary>
        /// Purge notifications older than <paramref name="daysOld"/> days (scheduled cleanup job).
        /// </summary>
        Task<StatusResponse> DeleteOldNotificationsAsync(int daysOld);
    }
}
