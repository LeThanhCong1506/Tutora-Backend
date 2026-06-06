using MV.DomainLayer.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Exceptions;
using System.Security.Claims;

namespace MV.PresentationLayer.Controllers;

[ApiController]
[Route("api/tutors/bookings")]
[Authorize]
public class TutorBookingController : ControllerBase
{
    private readonly IBookingService _bookingService;

    public TutorBookingController(IBookingService bookingService)
    {
        _bookingService = bookingService;
    }

    private string? TutorId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    [HttpGet]
    [Authorize(Roles = UserRole.Tutor)]
    public async Task<IActionResult> GetTutorBookingRequests(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? status = null)
    {
        var tutorId = TutorId;
        if (string.IsNullOrEmpty(tutorId))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));

        var result = await _bookingService.GetTutorBookingRequestsAsync(tutorId, page, pageSize, status);
        var payload = new
        {
            items = (List<BookingResponse>)result,
            totalCount = result.TotalCount,
            currentPage = result.CurrentPage,
            totalPages = result.TotalPages,
            pageSize = result.PageSize
        };
        return Ok(APIResponse<object>.Success(payload, ApiMessages.Success));
    }

    [HttpPost("{id}/accept")]
    public async Task<IActionResult> AcceptBooking(int id)
    {
        if (string.IsNullOrEmpty(TutorId))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));

        try
        {
            var result = await _bookingService.AcceptBookingAsync(TutorId, id);
            return Ok(APIResponse<TutorDecisionResponse>.Success(result, "Chấp nhận đặt lịch thành công."));
        }
        catch (BookingException ex)
        {
            return StatusCode(ex.HttpStatus, APIResponse.Fail(ex.Message, ex.HttpStatus));
        }
    }

    [HttpPost("{id}/decline")]
    public async Task<IActionResult> DeclineBooking(int id, [FromBody] TutorDecisionRequest request)
    {
        if (string.IsNullOrEmpty(TutorId))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));

        if (!ModelState.IsValid)
            return BadRequest(APIResponse.Fail("Dữ liệu đầu vào không hợp lệ.", 400));

        try
        {
            var result = await _bookingService.DeclineBookingAsync(TutorId, id, request.Reason);
            return Ok(APIResponse<BookingResponse>.Success(result, "Từ chối đặt lịch thành công."));
        }
        catch (BookingException ex)
        {
            return StatusCode(ex.HttpStatus, APIResponse.Fail(ex.Message, ex.HttpStatus));
        }
    }
}
