using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel.Assessment;
using MV.DomainLayer.DTO.ResponseModel.Assessment;
using MV.PresentationLayer.Helpers;

namespace MV.PresentationLayer.Controllers;

/// <summary>CRUD bộ đề đánh giá. Câu hỏi ở bảng riêng, không vào pool RAG.</summary>
[ApiController]
[Route("api/admin/assessments")]
// Admin-only, không đi qua hệ permission.
[Authorize(Roles = UserRole.Admin)]
public class AssessmentController : ControllerBase
{
    private readonly IAssessmentService _assessmentService;

    public AssessmentController(IAssessmentService assessmentService)
    {
        _assessmentService = assessmentService;
    }

    // Đề
    /// <summary>Tạo bộ đề (rỗng câu, thêm câu qua endpoint dưới).</summary>
    [HttpPost]
    public async Task<ActionResult<APIResponse<AssessmentResponse>>> Create(
        [FromBody] CreateAssessmentRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(APIResponse.Fail("Dữ liệu bộ đề không hợp lệ.", 400));

        var createdBy = UserHelper.GetUserId(User);
        var result = await _assessmentService.CreateAsync(request, createdBy, ct);
        return Ok(APIResponse<AssessmentResponse>.Success(result, "Tạo bộ đề thành công."));
    }

    /// <summary>Danh sách bộ đề, phân trang + filter + search.</summary>
    [HttpGet]
    public async Task<ActionResult<APIResponse<object>>> GetPaged(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] int? subjectId = null,
        [FromQuery] int? gradeLevelId = null,
        [FromQuery] string? status = null,
        [FromQuery] string? search = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDir = null,
        CancellationToken ct = default)
    {
        if (pageNumber < 1) pageNumber = 1;
        if (pageSize is < 1 or > 100) pageSize = 20;

        var paged = await _assessmentService.GetPagedAsync(
            pageNumber, pageSize, subjectId, gradeLevelId, status, search, sortBy, sortDir, ct);

        return Ok(APIResponse<object>.Success(new
        {
            items = paged.ToList(),
            paged.CurrentPage,
            paged.PageSize,
            paged.TotalPages,
            paged.TotalCount,
            paged.HasPrevious,
            paged.HasNext,
        }, "Lấy danh sách bộ đề thành công."));
    }

    /// <summary>Chi tiết đề kèm câu hỏi (có đáp án — bản cho admin).</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<APIResponse<AssessmentDetailResponse>>> GetById(Guid id, CancellationToken ct)
    {
        var result = await _assessmentService.GetByIdAsync(id, ct);
        return result == null
            ? NotFound(APIResponse.Fail("Không tìm thấy bộ đề.", 404))
            : Ok(APIResponse<AssessmentDetailResponse>.Success(result, "Lấy bộ đề thành công."));
    }

    /// <summary>Cập nhật cấu hình đề. Phát hành dùng endpoint status riêng.</summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<APIResponse<AssessmentResponse>>> Update(
        Guid id, [FromBody] UpdateAssessmentRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(APIResponse.Fail("Dữ liệu bộ đề không hợp lệ.", 400));

        var result = await _assessmentService.UpdateAsync(id, request, ct);
        return result == null
            ? NotFound(APIResponse.Fail("Không tìm thấy bộ đề.", 404))
            : Ok(APIResponse<AssessmentResponse>.Success(result, "Cập nhật bộ đề thành công."));
    }

    /// <summary>Phát hành / lưu trữ / về nháp.</summary>
    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<APIResponse<AssessmentResponse>>> UpdateStatus(
        Guid id, [FromBody] UpdateAssessmentStatusRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(APIResponse.Fail("Trạng thái không hợp lệ.", 400));

        var (result, error) = await _assessmentService.UpdateStatusAsync(id, request.Status, ct);
        if (error != null)
            return BadRequest(APIResponse.Fail(error, 400));

        return result == null
            ? NotFound(APIResponse.Fail("Không tìm thấy bộ đề.", 404))
            : Ok(APIResponse<AssessmentResponse>.Success(result, "Cập nhật trạng thái thành công."));
    }

    /// <summary>Xoá đề và toàn bộ câu hỏi của nó.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<APIResponse<object>>> Delete(Guid id, CancellationToken ct)
    {
        var ok = await _assessmentService.DeleteAsync(id, ct);
        return ok
            ? Ok(APIResponse<object>.Success("Xoá bộ đề thành công."))
            : NotFound(APIResponse.Fail("Không tìm thấy bộ đề.", 404));
    }

    // Câu hỏi trong đề

    /// <summary>Thêm câu vào đề. Bỏ trống displayOrder = thêm cuối.</summary>
    [HttpPost("{id:guid}/questions")]
    public async Task<ActionResult<APIResponse<AssessmentQuestionResponse>>> AddQuestion(
        Guid id, [FromBody] CreateAssessmentQuestionRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(APIResponse.Fail("Dữ liệu câu hỏi không hợp lệ.", 400));

        var (result, error) = await _assessmentService.AddQuestionAsync(id, request, ct);
        if (error != null)
            return BadRequest(APIResponse.Fail(error, 400));

        return result == null
            ? NotFound(APIResponse.Fail("Không tìm thấy bộ đề.", 404))
            : Ok(APIResponse<AssessmentQuestionResponse>.Success(result, "Thêm câu hỏi thành công."));
    }

    /// <summary>Sửa câu (thay toàn bộ nội dung).</summary>
    [HttpPut("{id:guid}/questions/{questionId:guid}")]
    public async Task<ActionResult<APIResponse<AssessmentQuestionResponse>>> UpdateQuestion(
        Guid id, Guid questionId, [FromBody] UpdateAssessmentQuestionRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(APIResponse.Fail("Dữ liệu câu hỏi không hợp lệ.", 400));

        var (result, error) = await _assessmentService.UpdateQuestionAsync(id, questionId, request, ct);
        if (error != null)
            return BadRequest(APIResponse.Fail(error, 400));

        return result == null
            ? NotFound(APIResponse.Fail("Không tìm thấy câu hỏi trong bộ đề.", 404))
            : Ok(APIResponse<AssessmentQuestionResponse>.Success(result, "Cập nhật câu hỏi thành công."));
    }

    /// <summary>Xoá câu, dồn lại thứ tự.</summary>
    [HttpDelete("{id:guid}/questions/{questionId:guid}")]
    public async Task<ActionResult<APIResponse<object>>> DeleteQuestion(
        Guid id, Guid questionId, CancellationToken ct)
    {
        var ok = await _assessmentService.DeleteQuestionAsync(id, questionId, ct);
        return ok
            ? Ok(APIResponse<object>.Success("Xoá câu hỏi thành công."))
            : NotFound(APIResponse.Fail("Không tìm thấy câu hỏi trong bộ đề.", 404));
    }

    /// <summary>Sắp lại thứ tự. Phải truyền đủ id mọi câu của đề.</summary>
    [HttpPut("{id:guid}/questions/reorder")]
    public async Task<ActionResult<APIResponse<object>>> ReorderQuestions(
        Guid id, [FromBody] ReorderAssessmentQuestionsRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(APIResponse.Fail("Danh sách câu hỏi không hợp lệ.", 400));

        var (ok, error) = await _assessmentService.ReorderQuestionsAsync(id, request.QuestionIds, ct);
        if (error != null)
            return BadRequest(APIResponse.Fail(error, 400));

        return ok
            ? Ok(APIResponse<object>.Success("Cập nhật thứ tự câu hỏi thành công."))
            : NotFound(APIResponse.Fail("Không tìm thấy bộ đề hoặc đề chưa có câu hỏi.", 404));
    }
}
