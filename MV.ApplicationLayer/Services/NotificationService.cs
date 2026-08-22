using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Hubs;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Constants;
using MV.DomainLayer.Helpers;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.RepositoryInterfaces;

namespace MV.ApplicationLayer.Services
{
    public class NotificationService : INotificationService
    {
        private readonly IUserRepository _userRepository;
        private readonly INotificationRepository _notificationRepository;
        private readonly IAppDbContext _dbContext;
        private readonly IHubContext<NotificationHub> _hubContext;
        private readonly IFirebasePushNotificationService _firebasePushNotificationService;
        private readonly ILogger<NotificationService> _logger;

        public NotificationService(
            IUserRepository userRepository,
            INotificationRepository notificationRepository,
            IAppDbContext dbContext,
            IHubContext<NotificationHub> hubContext,
            IFirebasePushNotificationService firebasePushNotificationService,
            ILogger<NotificationService> logger)
        {
            _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
            _notificationRepository = notificationRepository ?? throw new ArgumentNullException(nameof(notificationRepository));
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
            _firebasePushNotificationService = firebasePushNotificationService ?? throw new ArgumentNullException(nameof(firebasePushNotificationService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        private async Task SendPushAsync(User user, string title, string message, string? type, string? referenceId)
        {
            if (string.IsNullOrEmpty(user.Fcmtoken))
                return;

            await _firebasePushNotificationService.SendPushNotificationAsync(
                user.Fcmtoken,
                title,
                message,
                new Dictionary<string, string>
                {
                    { "type", type ?? "" },
                    { "referenceId", referenceId ?? "" }
                });
        }

        // CREATE
        public async Task<StatusResponse> CreateNotificationAsync(NotificationRequest request)
        {
            try
            {
                var user = await _userRepository.GetUserByIdAsync(request.Userid);
                if (user == null)
                {
                    return new StatusResponse
                    {
                        Status = NotificationStatus.Failed,
                        Message = "Không tìm thấy người dùng.",
                        Timestamp = TimeZoneHelper.UtcNow
                    };
                }

                var notification = new Notification
                {
                    Userid = request.Userid,
                    Title = request.Title,
                    Message = request.Message,
                    Type = request.Type,
                    Referenceid = request.Referenceid,
                    Isread = false,
                    Createdat = TimeZoneHelper.UtcNow
                };

                await _notificationRepository.CreateNotificationAsync(notification);
                await _dbContext.SaveChangesAsync();

                var notificationResponse = MapToResponse(notification);

                // Gửi realtime qua SignalR tới đúng user
                await _hubContext.Clients.Group($"user:{request.Userid}").SendAsync("ReceiveNotification", notificationResponse);

                // Gửi push notification qua FCM
                await SendPushAsync(user, notification.Title ?? "", notification.Message ?? "", notification.Type, notification.Referenceid);

                return new StatusResponse
                {
                    Status = NotificationStatus.Success,
                    Message = "Tạo thông báo thành công.",
                    Timestamp = TimeZoneHelper.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating notification for user {UserId}", request.Userid);
                return new StatusResponse
                {
                    Status = NotificationStatus.Failed,
                    Message = $"Lỗi khi tạo thông báo: {ex.Message}",
                    Timestamp = TimeZoneHelper.UtcNow
                };
            }
        }

        public async Task<StatusResponse> CreateNotificationsAsync(IEnumerable<NotificationRequest> requests)
        {
            try
            {
                if (requests == null || !requests.Any())
                {
                    return new StatusResponse
                    {
                        Status = NotificationStatus.Failed,
                        Message = "Không có thông báo nào để tạo.",
                        Timestamp = TimeZoneHelper.UtcNow
                    };
                }

                var notifications = new List<Notification>();

                foreach (var request in requests)
                {
                    var notification = new Notification
                    {
                        Userid = request.Userid,
                        Title = request.Title,
                        Message = request.Message,
                        Type = request.Type,
                        Referenceid = request.Referenceid,
                        Isread = false,
                        Createdat = TimeZoneHelper.UtcNow
                    };

                    notifications.Add(notification);
                }

                if (!notifications.Any())
                {
                    return new StatusResponse
                    {
                        Status = NotificationStatus.Failed,
                        Message = "Không có thông báo hợp lệ nào để tạo.",
                        Timestamp = TimeZoneHelper.UtcNow
                    };
                }

                await _notificationRepository.CreateNotificationsAsync(notifications);
                await _dbContext.SaveChangesAsync();

                // Gửi realtime qua SignalR tới từng user
                foreach (var notification in notifications)
                {
                    var response = MapToResponse(notification);
                    await _hubContext.Clients.Group($"user:{notification.Userid}").SendAsync("ReceiveNotification", response);

                    if (!string.IsNullOrEmpty(notification.Userid))
                    {
                        var recipient = await _userRepository.GetUserByIdAsync(notification.Userid);
                        if (recipient != null)
                        {
                            await SendPushAsync(recipient, notification.Title ?? "", notification.Message ?? "", notification.Type, notification.Referenceid);
                        }
                    }
                }

                return new StatusResponse
                {
                    Status = NotificationStatus.Success,
                    Message = $"Đã tạo và gửi {notifications.Count} thông báo thành công.",
                    Timestamp = TimeZoneHelper.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating bulk notifications");
                return new StatusResponse
                {
                    Status = NotificationStatus.Failed,
                    Message = $"Lỗi khi tạo thông báo hàng loạt: {ex.Message}",
                    Timestamp = TimeZoneHelper.UtcNow
                };
            }
        }

        // READ
        public async Task<NotificationResponse?> GetNotificationByIdAsync(int notificationId)
        {
            var notification = await _notificationRepository.GetNotificationByIdAsync(notificationId);
            if (notification == null)
                return null;

            return MapToResponse(notification);
        }

        public async Task<IEnumerable<NotificationResponse>> GetNotificationsByUserIdAsync(string userId)
        {
            var notifications = await _notificationRepository.GetNotificationsByUserIdAsync(userId);
            return notifications.Select(MapToResponse);
        }

        public async Task<IEnumerable<NotificationResponse>> GetUnreadNotificationsByUserIdAsync(string userId)
        {
            var notifications = await _notificationRepository.GetUnreadNotificationsByUserIdAsync(userId);
            return notifications.Select(MapToResponse);
        }

        public async Task<int> GetUnreadCountByUserIdAsync(string userId)
        {
            return await _notificationRepository.GetUnreadCountByUserIdAsync(userId);
        }

        public async Task<UnreadCountResponse> GetUnreadCountResponseByUserIdAsync(string userId)
        {
            var unreadCount = await _notificationRepository.GetUnreadCountByUserIdAsync(userId);
            var byType = await _notificationRepository.GetUnreadCountByTypeAsync(userId);

            return new UnreadCountResponse
            {
                UnreadCount = unreadCount,
                ByType = byType,
                Total = unreadCount
            };
        }

        public async Task<IEnumerable<NotificationResponse>> GetAllNotificationsAsync()
        {
            var notifications = await _notificationRepository.GetAllNotificationsAsync();
            return notifications.Select(MapToResponse);
        }

        // UPDATE
        public async Task<StatusResponse> MarkAsReadAsync(int notificationId, string currentUserId)
        {
            try
            {
                var notification = await _notificationRepository.GetNotificationByIdAsync(notificationId);
                if (notification == null)
                {
                    return new StatusResponse
                    {
                        Status = NotificationStatus.Failed,
                        Message = "Không tìm thấy thông báo.",
                        Timestamp = TimeZoneHelper.UtcNow
                    };
                }

                if (notification.Userid != currentUserId)
                {
                    return new StatusResponse
                    {
                        Status = NotificationStatus.Failed,
                        Message = "Bạn không có quyền đánh dấu thông báo này là đã đọc.",
                        Timestamp = TimeZoneHelper.UtcNow
                    };
                }

                await _notificationRepository.MarkAsReadAsync(notificationId);
                await _dbContext.SaveChangesAsync();

                // Push unread count update via SignalR so FE badge updates immediately
                var newCount = await _notificationRepository.GetUnreadCountByUserIdAsync(currentUserId);
                await _hubContext.Clients.Group($"user:{currentUserId}").SendAsync("NotificationCountUpdated", newCount);

                return new StatusResponse
                {
                    Status = NotificationStatus.Success,
                    Message = "Đã đánh dấu thông báo là đã đọc.",
                    Timestamp = TimeZoneHelper.UtcNow
                };
            }
            catch (Exception ex)
            {
                return new StatusResponse
                {
                    Status = NotificationStatus.Failed,
                    Message = $"Lỗi khi đánh dấu thông báo là đã đọc: {ex.Message}",
                    Timestamp = TimeZoneHelper.UtcNow
                };
            }
        }

        public async Task<StatusResponse> MarkAllAsReadAsync(string userId)
        {
            try
            {
                await _notificationRepository.MarkAllAsReadAsync(userId);

                // Push unread count = 0 via SignalR so FE badge clears immediately
                await _hubContext.Clients.Group($"user:{userId}").SendAsync("NotificationCountUpdated", 0);

                return new StatusResponse
                {
                    Status = NotificationStatus.Success,
                    Message = "Đã đánh dấu tất cả thông báo là đã đọc.",
                    Timestamp = TimeZoneHelper.UtcNow
                };
            }
            catch (Exception ex)
            {
                return new StatusResponse
                {
                    Status = NotificationStatus.Failed,
                    Message = $"Lỗi khi đánh dấu tất cả thông báo là đã đọc: {ex.Message}",
                    Timestamp = TimeZoneHelper.UtcNow
                };
            }
        }

        public async Task<StatusResponse> MarkAsReadByTypeAsync(string userId, string type)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(type))
                {
                    return new StatusResponse
                    {
                        Status = NotificationStatus.Failed,
                        Message = "Notification type is required.",
                        Timestamp = TimeZoneHelper.UtcNow
                    };
                }

                await _notificationRepository.MarkAsReadByTypeAsync(userId, type);

                var newCount = await _notificationRepository.GetUnreadCountByUserIdAsync(userId);
                await _hubContext.Clients.Group($"user:{userId}").SendAsync("NotificationCountUpdated", newCount);

                return new StatusResponse
                {
                    Status = NotificationStatus.Success,
                    Message = "Marked notifications of the selected type as read.",
                    Timestamp = TimeZoneHelper.UtcNow
                };
            }
            catch (Exception ex)
            {
                return new StatusResponse
                {
                    Status = NotificationStatus.Failed,
                    Message = $"Error marking notifications as read by type: {ex.Message}",
                    Timestamp = TimeZoneHelper.UtcNow
                };
            }
        }

        // DELETE
        public async Task<StatusResponse> DeleteNotificationAsync(int notificationId, string currentUserId)
        {
            try
            {
                var notification = await _notificationRepository.GetNotificationByIdAsync(notificationId);
                if (notification == null)
                {
                    return new StatusResponse
                    {
                        Status = NotificationStatus.Failed,
                        Message = "Không tìm thấy thông báo.",
                        Timestamp = TimeZoneHelper.UtcNow
                    };
                }

                if (notification.Userid != currentUserId)
                {
                    return new StatusResponse
                    {
                        Status = NotificationStatus.Failed,
                        Message = "Bạn không có quyền xóa thông báo này.",
                        Timestamp = TimeZoneHelper.UtcNow
                    };
                }

                var result = await _notificationRepository.DeleteNotificationAsync(notificationId);

                if (!result)
                {
                    return new StatusResponse
                    {
                        Status = NotificationStatus.Failed,
                        Message = "Xóa thông báo thất bại.",
                        Timestamp = TimeZoneHelper.UtcNow
                    };
                }

                await _dbContext.SaveChangesAsync();

                return new StatusResponse
                {
                    Status = NotificationStatus.Success,
                    Message = "Xóa thông báo thành công.",
                    Timestamp = TimeZoneHelper.UtcNow
                };
            }
            catch (Exception ex)
            {
                return new StatusResponse
                {
                    Status = NotificationStatus.Failed,
                    Message = $"Lỗi khi xóa thông báo: {ex.Message}",
                    Timestamp = TimeZoneHelper.UtcNow
                };
            }
        }

        public async Task<StatusResponse> DeleteAllNotificationsByUserIdAsync(string userId)
        {
            try
            {
                var result = await _notificationRepository.DeleteNotificationsByUserIdAsync(userId);

                if (!result)
                {
                    return new StatusResponse
                    {
                        Status = NotificationStatus.Failed,
                        Message = "Không tìm thấy thông báo nào để xóa.",
                        Timestamp = TimeZoneHelper.UtcNow
                    };
                }

                return new StatusResponse
                {
                    Status = NotificationStatus.Success,
                    Message = "Đã xóa tất cả thông báo thành công.",
                    Timestamp = TimeZoneHelper.UtcNow
                };
            }
            catch (Exception ex)
            {
                return new StatusResponse
                {
                    Status = NotificationStatus.Failed,
                    Message = $"Lỗi khi xóa tất cả thông báo: {ex.Message}",
                    Timestamp = TimeZoneHelper.UtcNow
                };
            }
        }

        public async Task<StatusResponse> DeleteOldNotificationsAsync(int daysOld)
        {
            try
            {
                if (daysOld < 1)
                {
                    return new StatusResponse
                    {
                        Status = NotificationStatus.Failed,
                        Message = "Số ngày phải ít nhất là 1.",
                        Timestamp = TimeZoneHelper.UtcNow
                    };
                }

                var beforeDate = TimeZoneHelper.UtcNow.AddDays(-daysOld);
                var result = await _notificationRepository.DeleteOldNotificationsAsync(beforeDate);

                if (!result)
                {
                    return new StatusResponse
                    {
                        Status = NotificationStatus.Success,
                        Message = "Không tìm thấy thông báo cũ nào để xóa.",
                        Timestamp = TimeZoneHelper.UtcNow
                    };
                }

                return new StatusResponse
                {
                    Status = NotificationStatus.Success,
                    Message = $"Đã xóa các thông báo cũ hơn {daysOld} ngày thành công.",
                    Timestamp = TimeZoneHelper.UtcNow
                };
            }
            catch (Exception ex)
            {
                return new StatusResponse
                {
                    Status = NotificationStatus.Failed,
                    Message = $"Lỗi khi xóa thông báo cũ: {ex.Message}",
                    Timestamp = TimeZoneHelper.UtcNow
                };
            }
        }

        // HELPER METHOD
        private static NotificationResponse MapToResponse(Notification notification)
        {
            return new NotificationResponse
            {
                Notificationid = notification.Notificationid,
                Userid = notification.Userid,
                Title = notification.Title,
                Message = notification.Message,
                Type = notification.Type,
                Referenceid = notification.Referenceid,
                Isread = notification.Isread,
                Createdat = notification.Createdat,
                // Username có thể null nếu user đăng ký bằng email/phone — fallback về Fullname
                Username     = notification.User?.Username ?? notification.User?.Fullname,
                UserFullname = notification.User?.Fullname
            };
        }
    }
}
