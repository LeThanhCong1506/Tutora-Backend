using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.ResponseModel.Question;
using MV.PresentationLayer.Helpers;

namespace MV.PresentationLayer.Controllers;

/// <summary>
/// Trang Tài nguyên công khai (study-resources) — list câu hỏi mẫu đã published
/// theo môn/chương + vote like/dislike. Xem được không cần đăng nhập; vote thì cần.
/// </summary>
[ApiController]
[Route("api/study-resources")]
public class StudyResourceController : ControllerBase
{
    private readonly IStudyResourceService _service;

    public StudyResourceController(IStudyResourceService service)
    {
        _service = service;
    }

    /// <summary>Chi tiết 1 câu theo id. GET /api/study-resources/questions/{questionId}</summary>
    [HttpGet("questions/{questionId:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetById(Guid questionId, CancellationToken ct)
    {
        var userId = User.Identity?.IsAuthenticated == true ? UserHelper.GetUserId(User) : null;
        var result = await _service.GetByIdAsync(questionId, userId, ct);
        return result == null
            ? NotFound(APIResponse.Fail("Không tìm thấy câu hỏi.", 404))
            : Ok(APIResponse<PublicQuestionResponse>.Success(result, "Lấy câu hỏi thành công."));
    }

    /// <summary>Câu hỏi theo môn. GET /api/study-resources/{subjectSlug}</summary>
    [HttpGet("{subjectSlug}")]
    [AllowAnonymous]
    public Task<IActionResult> GetBySubject(
        string subjectSlug,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
        => GetQuestions(subjectSlug, null, pageNumber, pageSize, ct);

    /// <summary>Câu hỏi theo môn + chương. GET /api/study-resources/{subjectSlug}/{chapterSlug}</summary>
    [HttpGet("{subjectSlug}/{chapterSlug}")]
    [AllowAnonymous]
    public Task<IActionResult> GetByChapter(
        string subjectSlug,
        string chapterSlug,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
        => GetQuestions(subjectSlug, chapterSlug, pageNumber, pageSize, ct);

    private async Task<IActionResult> GetQuestions(
        string subjectSlug, string? chapterSlug, int pageNumber, int pageSize, CancellationToken ct)
    {
        if (pageNumber < 1) pageNumber = 1;
        if (pageSize is < 1 or > 100) pageSize = 20;

        // Đăng nhập rồi thì kèm MyVote; khách vãng lai vẫn xem được.
        var userId = User.Identity?.IsAuthenticated == true ? UserHelper.GetUserId(User) : null;

        var paged = await _service.GetQuestionsAsync(subjectSlug, chapterSlug, pageNumber, pageSize, userId, ct);
        if (paged == null)
            return NotFound(APIResponse.Fail("Không tìm thấy môn học hoặc chương.", 404));

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

    /// <summary>Vote 1 câu. POST /api/study-resources/{questionId}/vote body { vote: 1 | -1 | 0 }</summary>
    [HttpPost("{questionId:guid}/vote")]
    [Authorize]
    public async Task<ActionResult<APIResponse<QuestionVoteResponse>>> Vote(
        Guid questionId, [FromBody] VoteRequest request, CancellationToken ct)
    {
        if (request.Vote is not (1 or -1 or 0))
            return BadRequest(APIResponse.Fail("Giá trị vote không hợp lệ (1, -1 hoặc 0).", 400));

        var userId = UserHelper.GetUserId(User);
        var result = await _service.VoteAsync(questionId, userId, request.Vote, ct);
        return result == null
            ? NotFound(APIResponse.Fail("Không tìm thấy câu hỏi.", 404))
            : Ok(APIResponse<QuestionVoteResponse>.Success(result, "Ghi nhận đánh giá thành công."));
    }

    public class VoteRequest
    {
        /// <summary>1 = like, -1 = dislike, 0 = bỏ vote.</summary>
        public int Vote { get; set; }
    }
}
