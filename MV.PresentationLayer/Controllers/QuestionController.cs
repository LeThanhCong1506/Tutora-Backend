using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel.Question;
using MV.DomainLayer.DTO.ResponseModel.Question;
using MV.PresentationLayer.Helpers;
using MV.PresentationLayer.Authorization;

namespace MV.PresentationLayer.Controllers;

/// <summary>
/// CRUD question bank cho staff/admin (CMS). Thêm/sửa câu hỏi tự động embed
/// content -> vector qua tutora-ai.
/// </summary>
[ApiController]
[Route("api/admin/questions")]
[Authorize]
public class QuestionController : ControllerBase
{
    private readonly IQuestionService _questionService;

    public QuestionController(IQuestionService questionService)
    {
        _questionService = questionService;
    }

    /// <summary>Tạo câu hỏi mới. Sau khi lưu, hệ thống embed content thành vector.</summary>
    [HttpPost]
    [RequirePermission(Permissions.QuestionBankCreate)]
    public async Task<ActionResult<APIResponse<QuestionResponse>>> Create(
        [FromBody] CreateQuestionRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(APIResponse.Fail("Dữ liệu câu hỏi không hợp lệ.", 400));

        var createdBy = UserHelper.GetUserId(User);
        var result = await _questionService.CreateAsync(request, createdBy, ct);
        return Ok(APIResponse<QuestionResponse>.Success(result, "Tạo câu hỏi thành công."));
    }

    /// <summary>Danh sách câu hỏi có phân trang + filter.</summary>
    [HttpGet]
    [RequirePermission(Permissions.QuestionBankView)]
    public async Task<ActionResult<APIResponse<object>>> GetPaged(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] int? subjectId = null,
        [FromQuery] int? gradeLevelId = null,
        [FromQuery] string? chapterIds = null,
        [FromQuery] string? reviewStatus = null,
        [FromQuery] string? search = null,
        [FromQuery] string? difficulties = null,
        [FromQuery] bool? hasSolution = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDir = null,
        [FromQuery] string? answerFormat = null,
        CancellationToken ct = default)
    {
        if (pageNumber < 1) pageNumber = 1;
        if (pageSize is < 1 or > 100) pageSize = 20;

        // Nhận CSV "1,2,3" -> list. Bỏ giá trị rỗng/không hợp lệ.
        var chapterIdList = ParseIntCsv(chapterIds);
        var difficultyList = ParseStrCsv(difficulties);

        var paged = await _questionService.GetPagedAsync(
            pageNumber, pageSize, subjectId, gradeLevelId, chapterIdList, reviewStatus, search,
            difficultyList, hasSolution, sortBy, sortDir, answerFormat, ct);

        return Ok(APIResponse<object>.Success(new
        {
            items = paged.ToList(),
            paged.CurrentPage,
            paged.PageSize,
            paged.TotalPages,
            paged.TotalCount,
            paged.HasPrevious,
            paged.HasNext,
        }, "Lấy danh sách câu hỏi thành công."));
    }

    private static List<int>? ParseIntCsv(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return null;
        var list = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var n) ? n : (int?)null)
            .Where(n => n.HasValue).Select(n => n!.Value).Distinct().ToList();
        return list.Count > 0 ? list : null;
    }

    private static List<string>? ParseStrCsv(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return null;
        var list = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct().ToList();
        return list.Count > 0 ? list : null;
    }

    /// <summary>Chi tiết 1 câu hỏi.</summary>
    [HttpGet("{id:guid}")]
    [RequirePermission(Permissions.QuestionBankView)]
    public async Task<ActionResult<APIResponse<QuestionResponse>>> GetById(Guid id, CancellationToken ct)
    {
        var result = await _questionService.GetByIdAsync(id, ct);
        return result == null
            ? NotFound(APIResponse.Fail("Không tìm thấy câu hỏi.", 404))
            : Ok(APIResponse<QuestionResponse>.Success(result, "Lấy câu hỏi thành công."));
    }

    /// <summary>Cập nhật câu hỏi. Nếu content đổi -> tự re-embed vector.</summary>
    [HttpPut("{id:guid}")]
    [RequirePermission(Permissions.QuestionBankUpdate)]
    public async Task<ActionResult<APIResponse<QuestionResponse>>> Update(
        Guid id, [FromBody] UpdateQuestionRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(APIResponse.Fail("Dữ liệu câu hỏi không hợp lệ.", 400));

        var result = await _questionService.UpdateAsync(id, request, ct);
        return result == null
            ? NotFound(APIResponse.Fail("Không tìm thấy câu hỏi.", 404))
            : Ok(APIResponse<QuestionResponse>.Success(result, "Cập nhật câu hỏi thành công."));
    }

    /// <summary>Xoá câu hỏi.</summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission(Permissions.QuestionBankDelete)]
    public async Task<ActionResult<APIResponse<object>>> Delete(Guid id, CancellationToken ct)
    {
        var ok = await _questionService.DeleteAsync(id, ct);
        return ok
            ? Ok(APIResponse<object>.Success("Xoá câu hỏi thành công."))
            : NotFound(APIResponse.Fail("Không tìm thấy câu hỏi.", 404));
    }
}
