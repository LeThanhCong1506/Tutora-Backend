using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.ResponseModel.Policy;

namespace MV.PresentationLayer.Controllers;

/// <summary>
/// Văn bản pháp lý công khai. Không yêu cầu đăng nhập: người dùng phải đọc được điều khoản
/// TRƯỚC khi tạo tài khoản, và các ô tick đồng ý ở màn đăng ký đều trỏ vào đây.
/// Chỉ trả bản `published` — nháp và bản lưu trữ không lọt ra ngoài.
/// </summary>
[ApiController]
[Route("api/policies")]
[AllowAnonymous]
public class PoliciesController : ControllerBase
{
    private readonly IPolicyService _service;

    public PoliciesController(IPolicyService service)
    {
        _service = service;
    }

    /// <summary>Danh sách văn bản đang phát hành, đã sắp theo thứ tự hiển thị.</summary>
    [HttpGet]
    public async Task<ActionResult<APIResponse<List<PolicyDocumentSummaryResponse>>>> GetPublished(CancellationToken ct)
    {
        var documents = await _service.GetPublishedAsync(ct);
        return Ok(APIResponse<List<PolicyDocumentSummaryResponse>>.Success(documents));
    }

    /// <summary>Chi tiết một văn bản theo slug (/terms, /privacy...), kèm nội dung Markdown.</summary>
    [HttpGet("{slug}")]
    public async Task<ActionResult<APIResponse<PolicyDocumentResponse>>> GetBySlug(string slug, CancellationToken ct)
    {
        var document = await _service.GetPublishedBySlugAsync(slug, ct);
        if (document is null)
            return NotFound(APIResponse<PolicyDocumentResponse>.Fail("Không tìm thấy văn bản chính sách.", 404));

        return Ok(APIResponse<PolicyDocumentResponse>.Success(document));
    }
}
