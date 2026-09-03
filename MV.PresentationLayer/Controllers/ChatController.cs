using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.RepositoryInterfaces;
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
    private readonly IPresenceService _presenceService;
    private readonly IChatRepository _chatRepository;
    private readonly ILogger<ChatController> _logger;
    private const string ChatImageBucket = "chat-images";
    private const long MaxImageSizeBytes = 5 * 1024 * 1024; // 5MB
    private const int MaxPresenceBatchSize = 50;

    public ChatController(
        IChatService chatService,
        IFileStorageService storageService,
        IPresenceService presenceService,
        IChatRepository chatRepository,
        ILogger<ChatController> logger)
    {
        _chatService = chatService;
        _storageService = storageService;
        _presenceService = presenceService;
        _chatRepository = chatRepository;
        _logger = logger;
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

    /// <summary>
    /// DELETE /api/chat/channels/{id}
    /// Xoá cuộc trò chuyện khỏi danh sách của NGƯỜI GỌI.
    /// </summary>
    [HttpDelete("channels/{id}")]
    public async Task<IActionResult> HideChannel(int id)
    {
        if (string.IsNullOrEmpty(UserId))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));

        try
        {
            await _chatService.HideChannelAsync(UserId, id);
            return Ok(APIResponse.Success("Đã xoá cuộc trò chuyện."));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, APIResponse.Fail(ex.Message, 403));
        }
    }

    /// <summary>
    /// GET /api/chat/presence/{targetUserId}
    /// Trạng thái hoạt động của một người dùng (online / offline + last-seen).
    /// FE gọi khi mở cuộc trò chuyện để hiển thị trạng thái ban đầu; cập nhật realtime
    /// sau đó nhận qua SignalR sự kiện "presenceChanged".
    /// </summary>
    [HttpGet("presence/{targetUserId}")]
    public async Task<IActionResult> GetPresence(string targetUserId)
    {
        SetPresenceResponseHeaders();

        var requesterUserId = UserId;
        if (string.IsNullOrEmpty(requesterUserId))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));

        if (string.IsNullOrWhiteSpace(targetUserId))
            return BadRequest(APIResponse.Fail("Thiếu mã người dùng.", 400));

        targetUserId = targetUserId.Trim();

        if (!string.Equals(requesterUserId, targetUserId, StringComparison.Ordinal) &&
            !await _chatRepository.AreActiveChatPartnersAsync(requesterUserId, targetUserId))
        {
            // Use the same 404 for a missing target and a non-partner to prevent user-id enumeration.
            return NotFound(APIResponse.Fail(
                "Không tìm thấy người dùng hoặc bạn không có quyền xem trạng thái.",
                404));
        }

        try
        {
            var result = await _presenceService.GetPresenceAsync(targetUserId);
            if (IsPresenceUnavailable(result))
                return PresenceUnavailable<UserPresenceResponse>();

            return Ok(APIResponse<UserPresenceResponse>.Success(
                result,
                "Lấy trạng thái hoạt động thành công."));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Presence backend unavailable while reading a single authorized target.");
            return PresenceUnavailable<UserPresenceResponse>();
        }
    }

    /// <summary>
    /// Returns presence for at most 50 users. Only self and active chat partners are
    /// retained; unauthorized IDs are silently filtered to avoid enumeration.
    /// </summary>
    [HttpPost("presence/batch")]
    public async Task<IActionResult> GetPresenceBatch([FromBody] PresenceBatchRequest? request)
    {
        SetPresenceResponseHeaders();

        var requesterUserId = UserId;
        if (string.IsNullOrEmpty(requesterUserId))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));

        if (request?.UserIds is null || request.UserIds.Count == 0)
            return BadRequest(APIResponse.Fail("Danh sách người dùng không được để trống.", 400));

        if (request.UserIds.Count > MaxPresenceBatchSize)
        {
            return BadRequest(APIResponse.Fail(
                $"Mỗi yêu cầu chỉ được tối đa {MaxPresenceBatchSize} người dùng.",
                400));
        }

        var requestedUserIds = request.UserIds
            .Select(id => id?.Trim())
            .Where(id => !string.IsNullOrWhiteSpace(id) && id!.Length <= 128)
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (requestedUserIds.Length == 0)
            return BadRequest(APIResponse.Fail("Danh sách người dùng không hợp lệ.", 400));

        var authorizedSet = (await _chatRepository.GetAuthorizedPresenceUserIdsAsync(
                requesterUserId,
                requestedUserIds))
            .ToHashSet(StringComparer.Ordinal);
        var authorizedUserIds = requestedUserIds
            .Where(authorizedSet.Contains)
            .ToArray();

        if (authorizedUserIds.Length == 0)
        {
            return Ok(APIResponse<IReadOnlyList<UserPresenceResponse>>.Success(
                [],
                "Không có người dùng được phép trong yêu cầu."));
        }

        try
        {
            var results = await _presenceService.GetPresencesAsync(
                authorizedUserIds,
                includeLastSeen: false);

            if (results.Count > 0 && results.All(IsPresenceUnavailable))
                return PresenceUnavailable<IReadOnlyList<UserPresenceResponse>>();

            return Ok(APIResponse<IReadOnlyList<UserPresenceResponse>>.Success(
                results,
                "Lấy trạng thái hoạt động thành công."));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Presence backend unavailable while reading an authorized batch.");
            return PresenceUnavailable<IReadOnlyList<UserPresenceResponse>>();
        }
    }

    private static bool IsPresenceUnavailable(UserPresenceResponse presence)
        => presence.IsOnline is null ||
           string.Equals(
               presence.Status,
               UserPresenceStatus.Unknown,
               StringComparison.OrdinalIgnoreCase);

    private ObjectResult PresenceUnavailable<T>()
    {
        Response.Headers.RetryAfter = "5";
        return StatusCode(
            StatusCodes.Status503ServiceUnavailable,
            APIResponse<T>.Fail(
                "Dịch vụ trạng thái tạm thời không khả dụng.",
                StatusCodes.Status503ServiceUnavailable,
                new { code = "presence_unavailable" }));
    }

    private void SetPresenceResponseHeaders()
    {
        Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
        Response.Headers.Pragma = "no-cache";
    }
}

public sealed class PresenceBatchRequest
{
    public List<string> UserIds { get; init; } = [];
}
