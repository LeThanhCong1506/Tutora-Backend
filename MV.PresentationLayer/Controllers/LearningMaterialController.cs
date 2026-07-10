using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Exceptions;
using MV.PresentationLayer.Helpers;

namespace MV.PresentationLayer.Controllers;

/// <summary>
/// Tài liệu học tập tutor chia sẻ cho học sinh của 1 booking (list/upload/delete theo bookingId).
/// </summary>
[ApiController]
[Route("api/bookings/{bookingId}/materials")]
[Authorize]
public class LearningMaterialController(ILearningMaterialService materialService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetMaterials(int bookingId)
    {
        try
        {
            var actorId = UserHelper.GetUserId(User);
            var result = await materialService.GetByBookingIdAsync(bookingId, actorId);
            return Ok(APIResponse<List<LearningMaterialResponse>>.Success(result, "Lấy danh sách tài liệu thành công."));
        }
        catch (NotFoundException ex)
        {
            return NotFound(APIResponse.Fail(ex.Message, 404));
        }
        catch (BadRequestException ex)
        {
            return BadRequest(APIResponse.Fail(ex.Message));
        }
    }

    [HttpPost]
    public async Task<IActionResult> UploadMaterial(int bookingId, [FromForm] UploadMaterialRequest request)
    {
        if (request.File == null || request.File.Length == 0)
            return BadRequest(APIResponse.Fail("Tệp tài liệu là bắt buộc."));
        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest(APIResponse.Fail("Tiêu đề tài liệu là bắt buộc."));

        try
        {
            var tutorId = UserHelper.GetUserId(User);
            var result = await materialService.UploadAsync(bookingId, tutorId, request.File, request.Title, request.Description, request.IsPublic);
            return Ok(APIResponse<LearningMaterialResponse>.Success(result, "Tải tài liệu lên thành công."));
        }
        catch (NotFoundException ex)
        {
            return NotFound(APIResponse.Fail(ex.Message, 404));
        }
        catch (BadRequestException ex)
        {
            return BadRequest(APIResponse.Fail(ex.Message));
        }
    }

    [HttpPatch("{materialId}")]
    public async Task<IActionResult> UpdateVisibility(int bookingId, int materialId, [FromBody] UpdateMaterialVisibilityRequest request)
    {
        try
        {
            var tutorId = UserHelper.GetUserId(User);
            var result = await materialService.UpdateVisibilityAsync(bookingId, materialId, tutorId, request.IsPublic);
            return Ok(APIResponse<LearningMaterialResponse>.Success(result, "Cập nhật tài liệu thành công."));
        }
        catch (NotFoundException ex)
        {
            return NotFound(APIResponse.Fail(ex.Message, 404));
        }
        catch (BadRequestException ex)
        {
            return BadRequest(APIResponse.Fail(ex.Message));
        }
    }

    [HttpDelete("{materialId}")]
    public async Task<IActionResult> DeleteMaterial(int bookingId, int materialId)
    {
        try
        {
            var tutorId = UserHelper.GetUserId(User);
            await materialService.DeleteAsync(bookingId, materialId, tutorId);
            return Ok(APIResponse.Success("Xoá tài liệu thành công."));
        }
        catch (NotFoundException ex)
        {
            return NotFound(APIResponse.Fail(ex.Message, 404));
        }
        catch (BadRequestException ex)
        {
            return BadRequest(APIResponse.Fail(ex.Message));
        }
    }
}
