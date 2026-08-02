using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.ResponseModel.KnowledgeBase;
using MV.DomainLayer.Exceptions;
using MV.PresentationLayer.Authorization;
using MV.PresentationLayer.Helpers;

namespace MV.PresentationLayer.Controllers;

/// <summary>
/// Knowledge Base Tutora - admin upload nội dung/chính sách (pdf/docx/xlsx/md) để bot
/// trả FAQ.
/// </summary>
[ApiController]
[Route("api/admin/knowledge-base")]
[Authorize]
public class KnowledgeBaseController : ControllerBase
{
    private readonly IKnowledgeBaseService _service;

    public KnowledgeBaseController(IKnowledgeBaseService service)
    {
        _service = service;
    }

    /// <summary>Upload 1 tài liệu (pdf/docx/xlsx/md, ≤20MB) → nạp vào Knowledge Base.</summary>
    [HttpPost("upload")]
    [RequirePermission(Permissions.KnowledgeBaseUpload)]
    [RequestSizeLimit(25_000_000)]   // ~25MB (service tự chặn 20MB, buffer thêm cho multipart)
    public async Task<ActionResult<APIResponse<KbUploadResponse>>> Upload(IFormFile file, CancellationToken ct)
    {
        if (file == null || file.Length == 0)
            return BadRequest(APIResponse.Fail("Vui lòng chọn file.", 400));

        try
        {
            var uploadedBy = UserHelper.GetUserId(User);
            var result = await _service.UploadAsync(file, uploadedBy, ct);
            return Ok(APIResponse<KbUploadResponse>.Success(
                result, $"Đã nạp \"{result.FileName}\" ({result.ChunkCount} đoạn) vào kiến thức."));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(APIResponse.Fail(ex.Message, 400));
        }
        catch (ExternalApiException ex)
        {
            return StatusCode(502, APIResponse.Fail(ex.Message, 502));
        }
    }

    /// <summary>Danh sách tài liệu KB đã nạp (mới nhất trước).</summary>
    [HttpGet("documents")]
    [RequirePermission(Permissions.KnowledgeBaseView)]
    public async Task<ActionResult<APIResponse<List<KbDocumentResponse>>>> GetDocuments(CancellationToken ct)
    {
        try
        {
            var docs = await _service.ListDocumentsAsync(ct);
            return Ok(APIResponse<List<KbDocumentResponse>>.Success(docs, "Lấy danh sách tài liệu thành công."));
        }
        catch (ExternalApiException ex)
        {
            return StatusCode(502, APIResponse.Fail(ex.Message, 502));
        }
    }

    /// <summary>Chi tiết 1 tài liệu + nội dung text (để xem trong modal CMS).</summary>
    [HttpGet("documents/{documentId}")]
    [RequirePermission(Permissions.KnowledgeBaseView)]
    public async Task<ActionResult<APIResponse<KbDocumentDetailResponse>>> GetDocumentDetail(string documentId, CancellationToken ct)
    {
        try
        {
            var detail = await _service.GetDocumentDetailAsync(documentId, ct);
            return detail == null
                ? NotFound(APIResponse.Fail("Không tìm thấy tài liệu.", 404))
                : Ok(APIResponse<KbDocumentDetailResponse>.Success(detail, "Lấy nội dung tài liệu thành công."));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(APIResponse.Fail(ex.Message, 400));
        }
    }

    /// <summary>Sửa nội dung tài liệu → chunk lại + re-embed.</summary>
    [HttpPut("documents/{documentId}/content")]
    [RequirePermission(Permissions.KnowledgeBaseUpload)]
    public async Task<ActionResult<APIResponse<KbDocumentDetailResponse>>> UpdateContent(
        string documentId, [FromBody] KbUpdateContentRequest body, CancellationToken ct)
    {
        try
        {
            var result = await _service.UpdateContentAsync(documentId, body.Content, ct);
            return Ok(APIResponse<KbDocumentDetailResponse>.Success(result, "Đã cập nhật nội dung tài liệu."));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(APIResponse.Fail(ex.Message, 400));
        }
        catch (ExternalApiException ex)
        {
            return StatusCode(502, APIResponse.Fail(ex.Message, 502));
        }
    }

    /// <summary>Xoá 1 tài liệu KB + toàn bộ chunk của nó.</summary>
    [HttpDelete("documents/{documentId}")]
    [RequirePermission(Permissions.KnowledgeBaseDelete)]
    public async Task<ActionResult<APIResponse<object>>> Delete(string documentId, CancellationToken ct)
    {
        try
        {
            await _service.DeleteDocumentAsync(documentId, ct);
            return Ok(APIResponse.Success("Đã xoá tài liệu khỏi Knowledge Base."));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(APIResponse.Fail(ex.Message, 400));
        }
        catch (ExternalApiException ex)
        {
            return StatusCode(502, APIResponse.Fail(ex.Message, 502));
        }
    }
}
