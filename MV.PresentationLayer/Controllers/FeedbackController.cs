using MV.DomainLayer.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.DTO.ResponseModel.Admin;
using MV.PresentationLayer.Helpers;
using MV.PresentationLayer.Authorization;

namespace MV.PresentationLayer.Controllers;

/// <summary>
/// Controller for feedback management - M3
/// </summary>
[ApiController]
[Route("api/feedbacks")]
[Authorize]
public class FeedbackController : ControllerBase
{
    private readonly IFeedbackService _feedbackService;

    public FeedbackController(IFeedbackService feedbackService)
    {
        _feedbackService = feedbackService;
    }

    /// <summary>
    /// Review a completed booking (parent/student). Mỗi người một đánh giá cho mỗi booking.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<APIResponse<FeedbackListResponse>>> CreateFeedback([FromBody] CreateFeedbackRequest request)
    {
        var parentId = UserHelper.GetUserId(User);
        var result = await _feedbackService.CreateFeedbackAsync(parentId, request);
        return Ok(APIResponse<FeedbackListResponse>.Success(result, "Tạo đánh giá thành công."));
    }

    /// <summary>
    /// Sửa đánh giá đã gửi. Chỉ tác giả, và chỉ khi gia sư chưa phản hồi.
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<APIResponse<FeedbackListResponse>>> UpdateFeedback(
        int id,
        [FromBody] UpdateFeedbackRequest request)
    {
        var userId = UserHelper.GetUserId(User);
        var result = await _feedbackService.UpdateFeedbackAsync(id, userId, request);
        return Ok(APIResponse<FeedbackListResponse>.Success(result, "Cập nhật đánh giá thành công."));
    }

    /// <summary>
    /// Đánh giá của một booking, cho chính người đánh giá xem lại kèm phản hồi của gia sư.
    /// </summary>
    [HttpGet("bookings/{id}")]
    public async Task<ActionResult<APIResponse<FeedbackListResponse?>>> GetBookingFeedback(int id)
    {
        var userId = UserHelper.GetUserId(User);
        var result = await _feedbackService.GetBookingFeedbackAsync(id, userId);
        return Ok(APIResponse<FeedbackListResponse?>.Success(result, "Lấy đánh giá thành công."));
    }

    /// <summary>
    /// Reply to feedback (tutor)
    /// </summary>
    [HttpPut("{id}/reply")]
    public async Task<ActionResult<APIResponse<FeedbackListResponse>>> ReplyFeedback(
        int id,
        [FromBody] ReplyFeedbackRequest request)
    {
        var tutorId = UserHelper.GetUserId(User);
        var result = await _feedbackService.ReplyFeedbackAsync(id, tutorId, request);
        return Ok(APIResponse<FeedbackListResponse>.Success(result, "Gửi phản hồi thành công."));
    }

    /// <summary>
    /// Get tutor's feedback list
    /// </summary>
    [HttpGet("tutors/{id}")]
    [AllowAnonymous]
    public async Task<ActionResult<APIResponse<PagedList<FeedbackListResponse>>>> GetTutorFeedback(
        string id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        var result = await _feedbackService.GetTutorFeedbacksAsync(id, page, pageSize);
        return Ok(APIResponse<PagedList<FeedbackListResponse>>.Success(result, "Lấy danh sách đánh giá thành công."));
    }

    /// <summary>
    /// Get tutor's feedback statistics
    /// </summary>
    [HttpGet("tutors/{id}/stats")]
    [AllowAnonymous]
    public async Task<ActionResult<APIResponse<FeedbackStatsResponse>>> GetTutorStats(string id)
    {
        var result = await _feedbackService.GetTutorFeedbackStatsAsync(id);
        return Ok(APIResponse<FeedbackStatsResponse>.Success(result, "Lấy thống kê đánh giá thành công."));
    }

    /// <summary>
    /// Check if user can review a booking
    /// </summary>
    [HttpGet("eligibility/bookings/{id}")]
    public async Task<ActionResult<APIResponse<bool>>> CanLeaveBookingFeedback(int id)
    {
        var userId = UserHelper.GetUserId(User);
        var result = await _feedbackService.CanLeaveBookingFeedbackAsync(id, userId);
        return Ok(APIResponse<bool>.Success(result, "Kiểm tra thành công."));
    }

    /// <summary>
    /// Danh sách đánh giá cho CMS kiểm duyệt — gồm cả đánh giá đã bị ẩn.
    /// </summary>
    [HttpGet("admin")]
    [RequirePermission(Permissions.FeedbackView)]
    public async Task<ActionResult<APIResponse<AdminFeedbackListResponse>>> GetFeedbacksForAdmin(
        [FromQuery] string? tutorId = null,
        [FromQuery] int? rating = null,
        [FromQuery] bool? isVisible = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await _feedbackService.GetFeedbacksForAdminAsync(tutorId, rating, isVisible, page, pageSize);
        return Ok(APIResponse<AdminFeedbackListResponse>.Success(result, "Lấy danh sách đánh giá thành công."));
    }

    /// <summary>
    /// Toggle feedback visibility (kiểm duyệt)
    /// </summary>
    [HttpPut("{id}/visibility")]
    [RequirePermission(Permissions.FeedbackModerate)]
    public async Task<ActionResult<APIResponse<bool>>> ToggleVisibility(
        int id,
        [FromBody] ToggleFeedbackVisibilityRequest? request = null)
    {
        var adminId = UserHelper.GetUserId(User);
        var result = await _feedbackService.ToggleFeedbackVisibilityAsync(id, adminId, request?.Reason);
        return Ok(APIResponse<bool>.Success(result, "Đã thay đổi trạng thái hiển thị."));
    }
}
