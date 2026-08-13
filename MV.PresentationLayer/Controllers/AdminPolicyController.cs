using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel.Policy;
using MV.DomainLayer.DTO.RequestModel.Question;
using MV.DomainLayer.DTO.ResponseModel.Policy;
using MV.PresentationLayer.Authorization;
using MV.PresentationLayer.Helpers;

namespace MV.PresentationLayer.Controllers;

/// <summary>
/// CRUD văn bản pháp lý cho CMS. Văn bản mới luôn ở dạng nháp, phải bấm xuất bản mới lên
/// trang công khai — soạn dở một văn bản pháp lý mà lộ ra ngoài là rủi ro thật.
/// </summary>
[ApiController]
[Route("api/admin/policies")]
[Authorize]
public class AdminPolicyController : ControllerBase
{
    private readonly IPolicyService _service;

    public AdminPolicyController(IPolicyService service)
    {
        _service = service;
    }

    [HttpGet]
    [RequirePermission(Permissions.PolicyView)]
    public async Task<ActionResult<APIResponse<List<PolicyDocumentSummaryResponse>>>> GetAll(
        [FromQuery] bool includeArchived = false, CancellationToken ct = default)
    {
        var documents = await _service.GetAllAsync(includeArchived, ct);
        return Ok(APIResponse<List<PolicyDocumentSummaryResponse>>.Success(documents));
    }

    [HttpGet("{id:int}")]
    [RequirePermission(Permissions.PolicyView)]
    public async Task<ActionResult<APIResponse<PolicyDocumentResponse>>> GetById(int id, CancellationToken ct)
    {
        var document = await _service.GetByIdAsync(id, ct);
        if (document is null)
            return NotFound(APIResponse<PolicyDocumentResponse>.Fail("Không tìm thấy văn bản chính sách.", 404));

        return Ok(APIResponse<PolicyDocumentResponse>.Success(document));
    }

    [HttpPost]
    [RequirePermission(Permissions.PolicyManage)]
    public async Task<ActionResult<APIResponse<PolicyDocumentResponse>>> Create(
        [FromBody] CreatePolicyDocumentRequest request, CancellationToken ct)
    {
        try
        {
            var created = await _service.CreateAsync(request, UserHelper.GetUserId(User), ct);
            return Ok(APIResponse<PolicyDocumentResponse>.Success(created, "Đã tạo văn bản ở dạng nháp."));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(APIResponse<PolicyDocumentResponse>.Fail(ex.Message, 400));
        }
    }

    [HttpPut("{id:int}")]
    [RequirePermission(Permissions.PolicyManage)]
    public async Task<ActionResult<APIResponse<PolicyDocumentResponse>>> Update(
        int id, [FromBody] UpdatePolicyDocumentRequest request, CancellationToken ct)
    {
        try
        {
            var updated = await _service.UpdateAsync(id, request, UserHelper.GetUserId(User), ct);
            return Ok(APIResponse<PolicyDocumentResponse>.Success(updated, "Đã lưu văn bản."));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(APIResponse<PolicyDocumentResponse>.Fail(ex.Message, 404));
        }
    }

    /// <summary>Xuất bản hoặc gỡ về nháp.</summary>
    [HttpPatch("{id:int}/published")]
    [RequirePermission(Permissions.PolicyManage)]
    public async Task<ActionResult<APIResponse<PolicyDocumentResponse>>> SetPublished(
        int id, [FromQuery] bool publish, CancellationToken ct)
    {
        try
        {
            var updated = await _service.SetPublishedAsync(id, publish, UserHelper.GetUserId(User), ct);
            return Ok(APIResponse<PolicyDocumentResponse>.Success(
                updated, publish ? "Đã xuất bản văn bản." : "Đã gỡ văn bản khỏi trang công khai."));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(APIResponse<PolicyDocumentResponse>.Fail(ex.Message, 404));
        }
    }

    /// <summary>
    /// Gán lại thứ tự hiển thị cho cả danh sách (kéo-thả ở màn danh sách CMS). Đặt trước
    /// `{id:int}` không cần lo trùng route vì ràng buộc :int không khớp chuỗi "reorder".
    /// </summary>
    [HttpPut("reorder")]
    [RequirePermission(Permissions.PolicyManage)]
    public async Task<ActionResult<APIResponse<object>>> Reorder(
        [FromBody] ReorderRequest request, CancellationToken ct)
    {
        try
        {
            await _service.ReorderAsync(request, UserHelper.GetUserId(User), ct);
            return Ok(APIResponse.Success("Đã lưu thứ tự hiển thị."));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(APIResponse.Fail(ex.Message, 404));
        }
    }

    /// <summary>Xoá mềm (chuyển sang lưu trữ) — bước bắt buộc trước khi được phép xoá vĩnh viễn.</summary>
    [HttpDelete("{id:int}")]
    [RequirePermission(Permissions.PolicyManage)]
    public async Task<ActionResult<APIResponse<object>>> Archive(int id, CancellationToken ct)
    {
        try
        {
            await _service.ArchiveAsync(id, UserHelper.GetUserId(User), ct);
            return Ok(APIResponse.Success("Đã chuyển văn bản vào lưu trữ."));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(APIResponse.Fail(ex.Message, 404));
        }
    }

    /// <summary>Đưa văn bản khỏi kho lưu trữ, trả về dạng nháp.</summary>
    [HttpPatch("{id:int}/restore")]
    [RequirePermission(Permissions.PolicyManage)]
    public async Task<ActionResult<APIResponse<PolicyDocumentResponse>>> Restore(int id, CancellationToken ct)
    {
        try
        {
            var restored = await _service.RestoreAsync(id, UserHelper.GetUserId(User), ct);
            return Ok(APIResponse<PolicyDocumentResponse>.Success(
                restored, "Đã khôi phục văn bản về dạng nháp."));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(APIResponse<PolicyDocumentResponse>.Fail(ex.Message, 404));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(APIResponse<PolicyDocumentResponse>.Fail(ex.Message, 400));
        }
    }

    /// <summary>
    /// Xoá cứng khỏi hệ thống. Đường dẫn tách riêng khỏi DELETE thường (xoá mềm) để không ai
    /// gọi nhầm cái này khi chỉ định gỡ văn bản khỏi trang công khai.
    /// </summary>
    [HttpDelete("{id:int}/permanent")]
    [RequirePermission(Permissions.PolicyManage)]
    public async Task<ActionResult<APIResponse<object>>> DeletePermanently(int id, CancellationToken ct)
    {
        try
        {
            await _service.DeleteAsync(id, ct);
            return Ok(APIResponse.Success("Đã xoá vĩnh viễn văn bản khỏi hệ thống."));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(APIResponse.Fail(ex.Message, 404));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(APIResponse.Fail(ex.Message, 400));
        }
    }
}
