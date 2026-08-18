using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel.Assessment;
using MV.DomainLayer.DTO.ResponseModel.Assessment;
using System.Security.Claims;

namespace MV.PresentationLayer.Controllers;

/// <summary>
/// Học sinh làm đề + kết quả + profile trình độ. Dữ kiện thô đi qua /analysis-input cho AI,
/// kết quả ghi lại qua /analysis vào profile trình độ.
/// </summary>
[ApiController]
[Route("api/assessments")]
[Authorize]
public class AssessmentAttemptController : ControllerBase
{
    private readonly IAssessmentAttemptService _service;

    public AssessmentAttemptController(IAssessmentAttemptService service)
    {
        _service = service;
    }

    private string? UserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    /// <summary>Đề học sinh làm được. Lọc theo môn/lớp cho bước khảo sát đầu vào.</summary>
    [HttpGet("available")]
    public async Task<ActionResult<APIResponse<List<AvailableAssessmentResponse>>>> GetAvailable(
        [FromQuery] int? subjectId = null,
        [FromQuery] int? gradeLevelId = null,
        CancellationToken ct = default)
    {
        var result = await _service.GetAvailableAsync(subjectId, gradeLevelId, ct);
        return Ok(APIResponse<List<AvailableAssessmentResponse>>.Success(result, "Lấy danh sách đề thành công."));
    }

    /// <summary>Khảo sát xong -> random 1 đề khớp môn/lớp và bắt đầu luôn.</summary>
    [HttpPost("start-random")]
    public async Task<ActionResult<APIResponse<AttemptInProgressResponse>>> StartRandom(
        [FromBody] StartRandomAttemptRequest request, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(UserId))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));
        if (!ModelState.IsValid)
            return BadRequest(APIResponse.Fail("Dữ liệu khảo sát không hợp lệ.", 400));

        var (result, error) = await _service.StartRandomAsync(
            request.SubjectId, request.GradeLevelId, UserId, ct);
        if (error != null)
            return BadRequest(APIResponse.Fail(error, 400));

        return result == null
            ? NotFound(APIResponse.Fail("Không tìm thấy đề đánh giá.", 404))
            : Ok(APIResponse<AttemptInProgressResponse>.Success(result, "Bắt đầu làm bài."));
    }

    /// <summary>Bắt đầu làm đề, không kèm đáp án. Còn bài dở thì tiếp tục bài đó.</summary>
    [HttpPost("{assessmentId:guid}/attempts")]
    public async Task<ActionResult<APIResponse<AttemptInProgressResponse>>> Start(
        Guid assessmentId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(UserId))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));

        var (result, error) = await _service.StartAsync(assessmentId, UserId, ct);
        if (error != null)
            return BadRequest(APIResponse.Fail(error, 400));

        return result == null
            ? NotFound(APIResponse.Fail("Không tìm thấy đề đánh giá.", 404))
            : Ok(APIResponse<AttemptInProgressResponse>.Success(result, "Bắt đầu làm bài."));
    }

    /// <summary>Nộp bài — chấm ngay, AI phân tích sau.</summary>
    [HttpPost("attempts/{attemptId:guid}/submit")]
    public async Task<ActionResult<APIResponse<AttemptResultResponse>>> Submit(
        Guid attemptId, [FromBody] SubmitAttemptRequest request, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(UserId))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));
        if (!ModelState.IsValid)
            return BadRequest(APIResponse.Fail("Dữ liệu bài làm không hợp lệ.", 400));

        var (result, error) = await _service.SubmitAsync(attemptId, UserId, request, ct);
        if (error != null)
            return BadRequest(APIResponse.Fail(error, 400));

        return result == null
            ? NotFound(APIResponse.Fail("Không tìm thấy bài làm.", 404))
            : Ok(APIResponse<AttemptResultResponse>.Success(result, "Nộp bài thành công."));
    }

    /// <summary>Kết quả bài đã nộp. Đáp án chỉ trả nếu đề bật cho xem.</summary>
    [HttpGet("attempts/{attemptId:guid}")]
    public async Task<ActionResult<APIResponse<AttemptResultResponse>>> GetResult(
        Guid attemptId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(UserId))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));

        var result = await _service.GetResultAsync(attemptId, UserId, ct);
        return result == null
            ? NotFound(APIResponse.Fail("Không tìm thấy bài làm.", 404))
            : Ok(APIResponse<AttemptResultResponse>.Success(result, "Lấy kết quả thành công."));
    }

    /// <summary>Lịch sử làm bài.</summary>
    [HttpGet("attempts")]
    public async Task<ActionResult<APIResponse<object>>> GetHistory(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] int? subjectId = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(UserId))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));

        if (pageNumber < 1) pageNumber = 1;
        if (pageSize is < 1 or > 100) pageSize = 20;

        var paged = await _service.GetHistoryAsync(UserId, pageNumber, pageSize, subjectId, ct);

        return Ok(APIResponse<object>.Success(new
        {
            items = paged.ToList(),
            paged.CurrentPage,
            paged.PageSize,
            paged.TotalPages,
            paged.TotalCount,
            paged.HasPrevious,
            paged.HasNext,
        }, "Lấy lịch sử làm bài thành công."));
    }

    /// <summary>
    /// Dữ kiện thô gửi tutora-ai. Chỉ chủ sở hữu gọi được — chưa có internal key nên luồng
    /// là FE/BE lấy rồi đẩy sang AI, không phải AI gọi ngược vào.
    /// </summary>
    [HttpGet("attempts/{attemptId:guid}/analysis-input")]
    public async Task<ActionResult<APIResponse<AttemptAnalysisInputResponse>>> GetAnalysisInput(
        Guid attemptId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(UserId))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));

        var result = await _service.GetAnalysisInputAsync(attemptId, ct);
        if (result == null)
            return NotFound(APIResponse.Fail("Không tìm thấy bài làm đã nộp.", 404));
        if (result.UserId != UserId)
            return Forbid();

        return Ok(APIResponse<AttemptAnalysisInputResponse>.Success(result, "Lấy dữ kiện phân tích thành công."));
    }

    /// <summary>Chạy phân tích AI cho bài vừa nộp. BE tự gọi tutora-ai rồi ghi profile.</summary>
    [HttpPost("attempts/{attemptId:guid}/analyze")]
    public async Task<ActionResult<APIResponse<AttemptAnalysisResultResponse>>> Analyze(
        Guid attemptId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(UserId))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));

        var input = await _service.GetAnalysisInputAsync(attemptId, ct);
        if (input == null)
            return NotFound(APIResponse.Fail("Không tìm thấy bài làm đã nộp.", 404));
        if (input.UserId != UserId)
            return Forbid();

        var (result, error) = await _service.RunAnalysisAsync(attemptId, ct);
        if (error != null)
            return StatusCode(502, APIResponse.Fail(error, 502));

        return result == null
            ? NotFound(APIResponse.Fail("Không tìm thấy bài làm đã nộp.", 404))
            : Ok(APIResponse<AttemptAnalysisResultResponse>.Success(result, "Phân tích thành công."));
    }

    /// <summary>Ghi kết quả AI vào bài làm + profile trình độ.</summary>
    [HttpPost("attempts/{attemptId:guid}/analysis")]
    public async Task<ActionResult<APIResponse<object>>> SaveAnalysis(
        Guid attemptId, [FromBody] SaveAnalysisRequest request, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(UserId))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));

        var input = await _service.GetAnalysisInputAsync(attemptId, ct);
        if (input == null)
            return NotFound(APIResponse.Fail("Không tìm thấy bài làm đã nộp.", 404));
        if (input.UserId != UserId)
            return Forbid();

        var ok = await _service.SaveAnalysisAsync(attemptId, request, ct);
        return ok
            ? Ok(APIResponse<object>.Success("Đã lưu kết quả phân tích."))
            : BadRequest(APIResponse.Fail("Không lưu được kết quả phân tích.", 400));
    }

    /// <summary>AI lỗi — bài vẫn giữ điểm, retry được.</summary>
    [HttpPost("attempts/{attemptId:guid}/analysis/failed")]
    public async Task<ActionResult<APIResponse<object>>> MarkAnalysisFailed(
        Guid attemptId, [FromBody] MarkAnalysisFailedRequest request, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(UserId))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));

        var input = await _service.GetAnalysisInputAsync(attemptId, ct);
        if (input == null)
            return NotFound(APIResponse.Fail("Không tìm thấy bài làm đã nộp.", 404));
        if (input.UserId != UserId)
            return Forbid();

        var ok = await _service.MarkAnalysisFailedAsync(attemptId, request.Error ?? "unknown", ct);
        return ok
            ? Ok(APIResponse<object>.Success("Đã ghi nhận lỗi phân tích."))
            : NotFound(APIResponse.Fail("Không tìm thấy bài làm.", 404));
    }

    /// <summary>Profile trình độ của user hiện tại. AI giải bài + trang lộ trình đọc cái này.</summary>
    [HttpGet("me/proficiency")]
    public async Task<ActionResult<APIResponse<List<ProficiencyProfileResponse>>>> GetMyProficiency(
        [FromQuery] int? subjectId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(UserId))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));

        var result = await _service.GetProficiencyAsync(UserId, subjectId, ct);
        return Ok(APIResponse<List<ProficiencyProfileResponse>>.Success(result, "Lấy profile trình độ thành công."));
    }
}

/// <summary>Body chọn đề sau bước khảo sát.</summary>
public class StartRandomAttemptRequest
{
    [System.ComponentModel.DataAnnotations.Range(1, int.MaxValue, ErrorMessage = "Thiếu môn học")]
    public int SubjectId { get; set; }

    /// <summary>Null = không giới hạn lớp.</summary>
    public int? GradeLevelId { get; set; }
}

/// <summary>Body cho endpoint báo lỗi phân tích.</summary>
public class MarkAnalysisFailedRequest
{
    public string? Error { get; set; }
}
