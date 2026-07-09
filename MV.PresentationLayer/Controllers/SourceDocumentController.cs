using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.ResponseModel.Question;
using MV.PresentationLayer.Helpers;

namespace MV.PresentationLayer.Controllers;

/// <summary>
/// Upload PDF -> AI tách câu hỏi -> lưu vào question bank (pending_review).
/// Staff duyệt các câu qua QuestionController sau đó.
/// </summary>
[ApiController]
[Route("api/admin/question-documents")]
[Authorize]
public class SourceDocumentController : ControllerBase
{
    private readonly ISourceDocumentService _service;

    public SourceDocumentController(ISourceDocumentService service)
    {
        _service = service;
    }

    /// <summary>Upload 1 PDF (≤20 trang). AI đọc, tách câu, lưu chờ duyệt.</summary>
    [HttpPost("upload")]
    [RequestSizeLimit(30_000_000)]   // ~30MB
    public async Task<ActionResult<APIResponse<UploadPdfResponse>>> Upload(
        IFormFile file,
        [FromQuery] int? subjectId,
        [FromQuery] int? gradeLevelId,
        CancellationToken ct)
    {
        if (file == null || file.Length == 0)
            return BadRequest(APIResponse.Fail("Vui lòng chọn file PDF.", 400));

        try
        {
            var uploadedBy = UserHelper.GetUserId(User);
            var result = await _service.UploadAndExtractAsync(file, subjectId, gradeLevelId, uploadedBy, ct);
            return Ok(APIResponse<UploadPdfResponse>.Success(result, result.Message ?? "Xử lý PDF thành công."));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(APIResponse.Fail(ex.Message, 400));
        }
    }

    /// <summary>Lịch sử trích xuất — list các lần upload PDF (mới nhất trước).</summary>
    [HttpGet("history")]
    public async Task<ActionResult<APIResponse<object>>> GetHistory(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (pageNumber < 1) pageNumber = 1;
        if (pageSize is < 1 or > 100) pageSize = 20;

        var paged = await _service.GetHistoryAsync(pageNumber, pageSize, ct);
        return Ok(APIResponse<object>.Success(new
        {
            items = paged.ToList(),
            paged.CurrentPage,
            paged.PageSize,
            paged.TotalPages,
            paged.TotalCount,
        }, "Lấy lịch sử trích xuất thành công."));
    }

    /// <summary>Chi tiết 1 lần upload: file + các câu đã tách.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<APIResponse<SourceDocumentDetailResponse>>> GetDetail(Guid id, CancellationToken ct)
    {
        var result = await _service.GetDetailAsync(id, ct);
        return result == null
            ? NotFound(APIResponse.Fail("Không tìm thấy tài liệu.", 404))
            : Ok(APIResponse<SourceDocumentDetailResponse>.Success(result, "Lấy chi tiết thành công."));
    }
}
