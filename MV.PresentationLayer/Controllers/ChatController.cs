using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using System.Security.Claims;

namespace MV.PresentationLayer.Controllers;

[ApiController]
[Route("api/chat")]
[Authorize]
public class ChatController : ControllerBase
{
    private readonly IChatService _chatService;
    private readonly IFileStorageService _storageService;
    private const string ChatImageBucket = "chat-images";
    private const long MaxImageSizeBytes = 5 * 1024 * 1024; // 5MB

    public ChatController(IChatService chatService, IFileStorageService storageService)
    {
        _chatService = chatService;
        _storageService = storageService;
    }

    private string? UserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    [HttpGet("channels")]
    public async Task<IActionResult> GetMyChannels()
    {
        if (string.IsNullOrEmpty(UserId))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));

        var result = await _chatService.GetMyChannelsAsync(UserId);
        return Ok(APIResponse<List<ChatChannelListItemResponse>>.Success(result, "Lấy danh sách kênh thành công."));
    }

    [HttpGet("channels/{id}/messages")]
    public async Task<IActionResult> GetMessages(int id, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, [FromQuery] string? search = null)
    {
        if (string.IsNullOrEmpty(UserId))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));

        var result = await _chatService.GetMessagesAsync(UserId, id, page, pageSize, search);
        return Ok(APIResponse<PagedList<ChatMessageResponse>>.Success(result, "Lấy tin nhắn thành công."));
    }

    [HttpPost("channels/{id}/messages")]
    public async Task<IActionResult> SendMessage(int id, [FromBody] ChatMessageCreateRequest dto)
    {
        if (string.IsNullOrEmpty(UserId))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));

        if (!ModelState.IsValid)
            return BadRequest(APIResponse.Fail("Dữ liệu đầu vào không hợp lệ.", 400));

        var result = await _chatService.SendMessageAsync(UserId, id, dto);
        return Ok(APIResponse<ChatMessageResponse>.Success(result, "Gửi tin nhắn thành công."));
    }

    /// <summary>
    /// POST /api/chat/channels — Create or get existing channel with a tutor.
    /// Used by "CHAT TƯ VẤN" on TutorDetailPage (no booking required).
    /// </summary>
    [HttpPost("channels")]
    public async Task<IActionResult> CreateOrGetChannel([FromBody] CreateChannelRequest request)
    {
        if (string.IsNullOrEmpty(UserId))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));

        if (string.IsNullOrEmpty(request?.TutorId))
            return BadRequest(APIResponse.Fail("Mã gia sư là bắt buộc.", 400));

        // Determine if caller is student based on role claim
        var roleClaim = User.FindFirstValue(System.Security.Claims.ClaimTypes.Role);
        bool isStudent = roleClaim?.Equals(UserRole.Student, StringComparison.OrdinalIgnoreCase) == true;

        var channelId = await _chatService.GetOrCreateChannelAsync(UserId, request.TutorId, isStudent);

        return Ok(APIResponse<object>.Success(new { channelId }, "Tạo/lấy kênh chat thành công."));
    }

    /// <summary>
    /// POST /api/chat/channels/booking/:bookingId — Create or get the channel
    /// between the parent and tutor attached to a booking.
    /// </summary>
    [HttpPost("channels/booking/{bookingId:int}")]
    public async Task<IActionResult> CreateOrGetBookingChannel(int bookingId)
    {
        if (string.IsNullOrEmpty(UserId))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));

        var channelId = await _chatService.GetOrCreateChannelForBookingAsync(UserId, bookingId);
        return Ok(APIResponse<object>.Success(new { channelId }, "Tạo/lấy kênh chat cho booking thành công."));
    }

    [HttpPost("channels/{id}/images")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> UploadImage(int id, IFormFile file)
    {
        if (string.IsNullOrEmpty(UserId))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));

        if (file == null || file.Length == 0)
            return BadRequest(APIResponse.Fail("Không có file được chọn.", 400));

        if (file.Length > MaxImageSizeBytes)
            return BadRequest(APIResponse.Fail("Kích thước file vượt quá 5MB.", 400));

        if (!file.ContentType.StartsWith("image/"))
            return BadRequest(APIResponse.Fail("Chỉ chấp nhận file hình ảnh.", 400));

        await _storageService.EnsureBucketExistsAsync(ChatImageBucket);
        var imageUrl = await _storageService.UploadFileAsync(ChatImageBucket, UserId, file);

        var dto = new ChatMessageCreateRequest
        {
            Content = imageUrl,
            MessageType = ChatMessageType.Image
        };

        var result = await _chatService.SendMessageAsync(UserId, id, dto);
        return Ok(APIResponse<ChatMessageResponse>.Success(result, "Hình ảnh đã được gửi thành công."));
    }

    /// <summary>
    /// GET /api/chat/unread-total-count
    /// Returns total unread message count across all channels for the calling user.
    /// Lightweight — for notification badge display only.
    /// </summary>
    [HttpGet("unread-total-count")]
    public async Task<IActionResult> GetUnreadTotalCount()
    {
        if (string.IsNullOrEmpty(UserId))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));

        var count = await _chatService.GetUnreadTotalCountAsync(UserId);
        return Ok(APIResponse<object>.Success(new { unreadCount = count }, "Lấy số tin nhắn chưa đọc thành công."));
    }

    [HttpPut("channels/{id}/read")]
    public async Task<IActionResult> MarkAsRead(int id)
    {
        if (string.IsNullOrEmpty(UserId))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));

        await _chatService.MarkMessagesAsReadAsync(UserId, id);
        return Ok(APIResponse.Success("Đã đánh dấu tất cả tin nhắn là đã đọc."));
    }
}
