using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using System.Security.Claims;

namespace MV.PresentationLayer.Controllers
{
    [Route("api/tutor/availabilities")]
    [ApiController]
    [Authorize]
    public class TutorAvailabilityController : ControllerBase
    {
        private readonly ITutorAvailabilityService _availabilityService;

        public TutorAvailabilityController(ITutorAvailabilityService availabilityService)
        {
            _availabilityService = availabilityService;
        }

        /// <summary>
        /// Get tutor ID from JWT claims
        /// </summary>
        private string GetCurrentUserId()
        {
            return User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirstValue("sub")
                ?? throw new UnauthorizedAccessException("Không tìm thấy thông tin tài khoản trong token.");
        }

        /// <summary>
        /// Add multiple availability slots at once
        /// POST /api/tutor/availabilities/bulk
        /// </summary>
        [HttpPost("bulk")]
        public async Task<IActionResult> BulkAddAvailabilities([FromBody] BulkCreateAvailabilityRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(APIResponse<object>.Fail(ApiMessages.InvalidRequestData, 400, ModelState));
                }

                var tutorId = GetCurrentUserId();
                var result = await _availabilityService.BulkAddAvailabilitiesAsync(tutorId, request);

                return StatusCode(201, APIResponse<List<TutorAvailabilityResponse>>.Success(
                    result, 
                    $"Tạo thành công {result.Count} khung giờ dạy học.", 
                    201));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(APIResponse<object>.Fail(ex.Message, 400));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(APIResponse<object>.Fail(ex.Message, 400));
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(APIResponse<object>.Fail(ex.Message, 401));
            }
            catch (Exception ex)
            {
                return StatusCode(500, APIResponse<object>.Fail(ApiMessages.GenericErrorPrefix + ex.Message, 500));
            }
        }

        /// <summary>
        /// Add a single availability slot
        /// POST /api/tutor/availabilities
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> AddAvailability([FromBody] CreateAvailabilityRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(APIResponse<object>.Fail(ApiMessages.InvalidRequestData, 400, ModelState));
                }

                var tutorId = GetCurrentUserId();
                var result = await _availabilityService.AddAvailabilityAsync(tutorId, request);

                return StatusCode(201, APIResponse<TutorAvailabilityResponse>.Success(result, "Tạo khung giờ dạy học thành công.", 201));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(APIResponse<object>.Fail(ex.Message, 400));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(APIResponse<object>.Fail(ex.Message, 400));
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(APIResponse<object>.Fail(ex.Message, 401));
            }
            catch (Exception ex)
            {
                return StatusCode(500, APIResponse<object>.Fail(ApiMessages.GenericErrorPrefix + ex.Message, 500));
            }
        }

        /// <summary>
        /// Get all availability slots for the current tutor
        /// GET /api/tutor/availabilities
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetAvailabilities()
        {
            try
            {
                var tutorId = GetCurrentUserId();
                var result = await _availabilityService.GetAvailabilitiesAsync(tutorId);

                return Ok(APIResponse<List<TutorAvailabilityResponse>>.Success(result, "Lấy danh sách khung giờ thành công."));
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(APIResponse<object>.Fail(ex.Message, 401));
            }
            catch (Exception ex)
            {
                return StatusCode(500, APIResponse<object>.Fail(ApiMessages.GenericErrorPrefix + ex.Message, 500));
            }
        }

        /// <summary>
        /// Get availability slots for a specific tutor (public - for parents to view)
        /// GET /api/tutor/availabilities/{tutorId}
        /// </summary>
        [HttpGet("{id}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetTutorAvailabilities(string id)
        {
            try
            {
                var result = await _availabilityService.GetAvailabilitiesAsync(id);

                return Ok(APIResponse<List<TutorAvailabilityResponse>>.Success(result, "Lấy danh sách khung giờ của gia sư thành công."));
            }
            catch (Exception ex)
            {
                return StatusCode(500, APIResponse<object>.Fail(ApiMessages.GenericErrorPrefix + ex.Message, 500));
            }
        }

        /// <summary>
        /// Delete multiple availability slots at once
        /// DELETE /api/tutor/availabilities/bulk
        /// </summary>
        [HttpDelete("bulk")]
        public async Task<IActionResult> BulkDeleteAvailabilities([FromBody] BulkDeleteAvailabilityRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(APIResponse<object>.Fail(ApiMessages.InvalidRequestData, 400, ModelState));
                }

                var tutorId = GetCurrentUserId();
                var deletedCount = await _availabilityService.BulkDeleteAvailabilitiesAsync(tutorId, request);

                if (deletedCount == 0)
                {
                    return NotFound(APIResponse<object>.Fail("Không tìm thấy khung giờ nào để xóa.", 404));
                }

                return Ok(APIResponse<object>.Success(
                    new { deletedCount }, 
                    $"Đã xóa thành công {deletedCount} khung giờ."));
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(APIResponse<object>.Fail(ex.Message, 409));
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, APIResponse<object>.Fail(ex.Message, 403));
            }
            catch (Exception ex)
            {
                return StatusCode(500, APIResponse<object>.Fail(ApiMessages.GenericErrorPrefix + ex.Message, 500));
            }
        }

        /// <summary>
        /// Delete a single availability slot
        /// DELETE /api/tutor/availabilities/{id}
        /// </summary>
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> DeleteAvailability(int id)
        {
            try
            {
                var tutorId = GetCurrentUserId();
                var result = await _availabilityService.DeleteAvailabilityAsync(tutorId, id);

                if (!result)
                {
                    return NotFound(APIResponse<object>.Fail("Không tìm thấy khung giờ.", 404));
                }

                return NoContent();
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(APIResponse<object>.Fail(ex.Message, 409));
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, APIResponse<object>.Fail(ex.Message, 403));
            }
            catch (Exception ex)
            {
                return StatusCode(500, APIResponse<object>.Fail(ApiMessages.GenericErrorPrefix + ex.Message, 500));
            }
        }
    }
}
