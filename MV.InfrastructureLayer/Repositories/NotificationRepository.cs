using Microsoft.EntityFrameworkCore;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer.DBContext;
using MV.ApplicationLayer.RepositoryInterfaces;

namespace MV.InfrastructureLayer.Repositories
{
    public class NotificationRepository : INotificationRepository
    {
        private readonly AgoraDbContext _context;

        public NotificationRepository(AgoraDbContext context)
            => _context = context ?? throw new ArgumentNullException(nameof(context));

        // Create
        public async Task CreateNotificationAsync(Notification notification)
            => await _context.Notifications.AddAsync(notification);

        public async Task CreateNotificationsAsync(IEnumerable<Notification> notifications)
            => await _context.Notifications.AddRangeAsync(notifications);

        // Read
        public Task<Notification?> GetNotificationByIdAsync(int notificationId)
            => _context.Notifications
                .Include(n => n.User)
                .FirstOrDefaultAsync(n => n.Notificationid == notificationId);

        public async Task<IEnumerable<Notification>> GetNotificationsByUserIdAsync(string userId)
            => await _context.Notifications
                .Include(n => n.User)
                .Where(n => n.Userid == userId)
                .OrderByDescending(n => n.Createdat)
                .AsNoTracking()
                .ToListAsync();

        public async Task<IEnumerable<Notification>> GetUnreadNotificationsByUserIdAsync(string userId)
            => await _context.Notifications
                .Include(n => n.User)
                .Where(n => n.Userid == userId && n.Isread == false)
                .OrderByDescending(n => n.Createdat)
                .AsNoTracking()
                .ToListAsync();

        public Task<int> GetUnreadCountByUserIdAsync(string userId)
            => _context.Notifications
                .CountAsync(n => n.Userid == userId && n.Isread == false);

        public Task<Dictionary<string, int>> GetUnreadCountByTypeAsync(string userId)
            => _context.Notifications
                .Where(n => n.Userid == userId &&
                            n.Isread == false &&
                            !string.IsNullOrEmpty(n.Type))
                .GroupBy(n => n.Type!)
                .ToDictionaryAsync(g => g.Key, g => g.Count());

        public Task<bool> ExistsByUserAndTypeAndReferenceAsync(string userId, string type, string? referenceId)
            => _context.Notifications.AnyAsync(n =>
                n.Userid == userId &&
                n.Type == type &&
                n.Referenceid == referenceId);

        public async Task<IEnumerable<Notification>> GetAllNotificationsAsync()
            => await _context.Notifications
                .Include(n => n.User)
                .OrderByDescending(n => n.Createdat)
                .AsNoTracking()
                .ToListAsync();

        // Update
        public async Task MarkAsReadAsync(int notificationId)
        {
            var notification = await _context.Notifications.FindAsync(notificationId);
            if (notification != null)
            {
                notification.Isread = true;
            }
        }

        public async Task MarkAllAsReadAsync(string userId)
        {
            await _context.Notifications
                .Where(n => n.Userid == userId && n.Isread == false)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.Isread, true));
        }

        public Task<int> MarkAsReadByTypeAsync(string userId, string type)
            => _context.Notifications
                .Where(n => n.Userid == userId && n.Isread == false && n.Type == type)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.Isread, true));

        // Delete
        public async Task<bool> DeleteNotificationAsync(int notificationId)
        {
            var notification = await _context.Notifications.FindAsync(notificationId);
            if (notification == null) return false;
            _context.Notifications.Remove(notification);
            return true;
        }

        public async Task<bool> DeleteNotificationsByUserIdAsync(string userId)
        {
            var count = await _context.Notifications
                .Where(n => n.Userid == userId)
                .ExecuteDeleteAsync();
            return count > 0;
        }

        public async Task<bool> DeleteOldNotificationsAsync(DateTime beforeDate)
        {
            var count = await _context.Notifications
                .Where(n => n.Createdat < beforeDate)
                .ExecuteDeleteAsync();
            return count > 0;
        }
    }
}
