using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.RepositoryInterfaces
{
    public interface INotificationRepository
    {
        // Create
        Task CreateNotificationAsync(Notification notification);
        Task CreateNotificationsAsync(IEnumerable<Notification> notifications);

        // Read
        Task<Notification?> GetNotificationByIdAsync(int notificationId);
        Task<IEnumerable<Notification>> GetNotificationsByUserIdAsync(string userId);
        Task<IEnumerable<Notification>> GetUnreadNotificationsByUserIdAsync(string userId);
        Task<int> GetUnreadCountByUserIdAsync(string userId);
        Task<Dictionary<string, int>> GetUnreadCountByTypeAsync(string userId);
        Task<IEnumerable<Notification>> GetAllNotificationsAsync();

        // Update
        Task MarkAsReadAsync(int notificationId);
        Task MarkAllAsReadAsync(string userId);
        Task<int> MarkAsReadByTypeAsync(string userId, string type);

        // Delete
        Task<bool> DeleteNotificationAsync(int notificationId);
        Task<bool> DeleteNotificationsByUserIdAsync(string userId);
        Task<bool> DeleteOldNotificationsAsync(DateTime beforeDate);
    }
}
