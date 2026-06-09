using MV.DomainLayer.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.ApplicationLayer.Services;
using MV.ApplicationLayer.Interfaces;
using MV.DomainLayer.DTO.RequestModel;
using System.Security.Claims;

namespace MV.PresentationLayer.Controllers
{
    [Route("api/notifications")]
    [ApiController]
    public class NotificationsController : ControllerBase
    {
        private readonly INotificationService _notificationService;
        private readonly IFirebasePushNotificationService _firebasePushNotificationService;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<NotificationsController> _logger;

        public NotificationsController(
            INotificationService notificationService,
            IFirebasePushNotificationService firebasePushNotificationService,
            IUnitOfWork unitOfWork,
            ILogger<NotificationsController> logger)
        {
            _notificationService = notificationService;
            _firebasePushNotificationService = firebasePushNotificationService;
            _unitOfWork = unitOfWork;
            _logger = logger;
        }

        /// <summary>
        /// [TEST] Gửi thẳng một push notification qua FCM tới user hiện tại (đọc Fcmtoken từ DB),
        /// không tạo bản ghi Notification — dùng để kiểm tra cấu hình Firebase + FCM token trên thiết bị.
        /// </summary>
        [HttpPost("test/fcm")]
        [Authorize]
        public async Task<IActionResult> TestSendFcmPush([FromBody] NotificationRequest request)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var user = await _unitOfWork.UserRepository.GetUserByIdAsync(userId);
            if (user == null)
                return NotFound(new { message = "Không tìm thấy user." });

            if (string.IsNullOrEmpty(user.Fcmtoken))
                return BadRequest(new { message = "User chưa có Fcmtoken — hãy đăng nhập trên mobile để đăng ký token trước." });

            var sent = await _firebasePushNotificationService.SendPushNotificationAsync(
                user.Fcmtoken,
                request.Title,
                request.Message,
                new Dictionary<string, string>
                {
                    { "type", request.Type ?? "" },
                    { "referenceId", request.Referenceid ?? "" }
                });

            if (!sent)
                return StatusCode(500, new { message = "Gửi push notification thất bại — kiểm tra log để biết chi tiết." });

            return Ok(new { message = "Đã gửi push notification thành công." });
        }

        /// <summary>
        /// Tạo notification mới và gửi push notification đến user
        /// </summary>
        [HttpPost]
        [Authorize(Roles = UserRole.AdminOrStaff)]
        public async Task<IActionResult> CreateNotification([FromBody] NotificationRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var result = await _notificationService.CreateNotificationAsync(request);

            if (result.Status == NotificationStatus.Failed)
                return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Tạo nhiều notifications cùng lúc
        /// </summary>
        [HttpPost("bulk")]
        [Authorize(Roles = UserRole.AdminOrStaff)]
        public async Task<IActionResult> CreateNotifications([FromBody] IEnumerable<NotificationRequest> requests)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var result = await _notificationService.CreateNotificationsAsync(requests);

            if (result.Status == NotificationStatus.Failed)
                return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Lấy tất cả notifications của user hiện tại
        /// </summary>
        [HttpGet("mine")]
        [Authorize]
        public async Task<IActionResult> GetMyNotifications()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var notifications = await _notificationService.GetNotificationsByUserIdAsync(userId);
            return Ok(notifications);
        }

        /// <summary>
        /// Lấy notifications chưa đọc của user hiện tại
        /// </summary>
        [HttpGet("mine/unread")]
        [Authorize]
        public async Task<IActionResult> GetMyUnreadNotifications()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var notifications = await _notificationService.GetUnreadNotificationsByUserIdAsync(userId);
            return Ok(notifications);
        }

        /// <summary>
        /// Đếm số notifications chưa đọc
        /// </summary>
        [HttpGet("mine/unread-count")]
        [Authorize]
        public async Task<IActionResult> GetUnreadCount()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var count = await _notificationService.GetUnreadCountResponseByUserIdAsync(userId);
            return Ok(count);
        }

        /// <summary>
        /// Mark all unread notifications of the current user for one non-null notification type as read.
        /// Legacy notifications with null/empty type are not affected.
        /// </summary>
        [HttpPut("mine/read-by-type/{type}")]
        [Authorize]
        public async Task<IActionResult> MarkAsReadByType(string type)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var result = await _notificationService.MarkAsReadByTypeAsync(userId, type);

            if (result.Status == NotificationStatus.Failed)
                return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Lấy một notification theo id
        /// - Chỉ owner hoặc Admin/Staff được phép xem
        /// </summary>
        [HttpGet("{id}")]
        [Authorize]
        public async Task<IActionResult> GetNotification(int id)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(currentUserId))
                return Unauthorized();

            var notification = await _notificationService.GetNotificationByIdAsync(id);
            if (notification == null)
                return NotFound();

            // owner or Admin/Staff only
            if (notification.Userid != currentUserId && !User.IsInRole(UserRole.Admin) && !User.IsInRole(UserRole.Staff))
                return Forbid();

            return Ok(notification);
        }

        /// <summary>
        /// Lấy tất cả notifications (Admin/Staff)
        /// </summary>
        [HttpGet("all")]
        [Authorize(Roles = UserRole.AdminOrStaff)]
        public async Task<IActionResult> GetAllNotifications()
        {
            var notifications = await _notificationService.GetAllNotificationsAsync();
            return Ok(notifications);
        }

        /// <summary>
        /// Lấy notifications của một user cụ thể (Admin/Staff)
        /// </summary>
        [HttpGet("users/{id}")]
        [Authorize(Roles = UserRole.AdminOrStaff)]
        public async Task<IActionResult> GetNotificationsByUserId(string id)
        {
            if (string.IsNullOrEmpty(id))
                return BadRequest(new { message = "ID người dùng là bắt buộc." });

            var notifications = await _notificationService.GetNotificationsByUserIdAsync(id);
            return Ok(notifications);
        }

        /// <summary>
        /// Đánh dấu notification là đã đọc
        /// </summary>
        [HttpPut("{id}/read")]
        [Authorize]
        public async Task<IActionResult> MarkAsRead(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var result = await _notificationService.MarkAsReadAsync(id, userId);

            if (result.Status == NotificationStatus.Failed)
                return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Đánh dấu tất cả notifications là đã đọc
        /// </summary>
        [HttpPut("read-all")]
        [Authorize]
        public async Task<IActionResult> MarkAllAsRead()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var result = await _notificationService.MarkAllAsReadAsync(userId);
            return Ok(result);
        }

        /// <summary>
        /// Xóa notification
        /// </summary>
        [HttpDelete("{id}")]
        [Authorize]
        public async Task<IActionResult> DeleteNotification(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var result = await _notificationService.DeleteNotificationAsync(id, userId);

            if (result.Status == NotificationStatus.Failed)
                return NotFound(result);

            return Ok(result);
        }

        /// <summary>
        /// Xóa tất cả notifications của user hiện tại
        /// </summary>
        [HttpDelete("mine")]
        [Authorize]
        public async Task<IActionResult> DeleteMyNotifications()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var result = await _notificationService.DeleteAllNotificationsByUserIdAsync(userId);

            if (result.Status == NotificationStatus.Failed)
                return NotFound(result);

            return Ok(result);
        }

        /// <summary>
        /// Xóa tất cả notifications của một user cụ thể (Admin/Staff)
        /// </summary>
        [HttpDelete("users/{id}")]
        [Authorize(Roles = UserRole.AdminOrStaff)]
        public async Task<IActionResult> DeleteNotificationsByUserId(string id)
        {
            if (string.IsNullOrEmpty(id))
                return BadRequest(new { message = "ID người dùng là bắt buộc." });

            var result = await _notificationService.DeleteAllNotificationsByUserIdAsync(id);

            if (result.Status == NotificationStatus.Failed)
                return NotFound(result);

            return Ok(result);
        }

        /// <summary>
        /// Xóa notifications cũ hơn X ngày (Admin/Staff)
        /// </summary>
        [HttpDelete("old/{daysOld}")]
        [Authorize(Roles = UserRole.AdminOrStaff)]
        public async Task<IActionResult> DeleteOldNotifications(int daysOld)
        {
            if (daysOld < 1)
                return BadRequest(new { message = "Số ngày phải ít nhất là 1." });

            var result = await _notificationService.DeleteOldNotificationsAsync(daysOld);
            return Ok(result);
        }
    }
}
