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
}
