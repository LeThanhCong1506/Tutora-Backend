using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel.Question;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.DTO.ResponseModel.Question;

namespace MV.PresentationLayer.Controllers;

/// <summary>
/// CRUD danh mục (môn học / khối lớp / chương / loại câu hỏi) cho CMS admin.
/// </summary>
[ApiController]
[Route("api/admin/lookup")]
[Authorize]
public class AdminLookupController : ControllerBase
{
    private readonly ILookupService _lookup;

    public AdminLookupController(ILookupService lookup)
    {
        _lookup = lookup;
    }

    // Subjects
    [HttpPost("subjects")]
    public async Task<ActionResult<APIResponse<SubjectResponse>>> CreateSubject(
        [FromBody] SubjectRequest req)
    {
        if (!ModelState.IsValid) return BadRequest(APIResponse.Fail("Dữ liệu môn học không hợp lệ.", 400));
        var result = await _lookup.CreateSubjectAsync(req);
        return Ok(APIResponse<SubjectResponse>.Success(result, "Tạo môn học mới thành công."));
    }

    [HttpPut("subjects/{id:int}")]
    public async Task<ActionResult<APIResponse<SubjectResponse>>> UpdateSubject(
        int id, [FromBody] SubjectRequest req)
    {
        if (!ModelState.IsValid) return BadRequest(APIResponse.Fail("Dữ liệu môn học không hợp lệ.", 400));
        var result = await _lookup.UpdateSubjectAsync(id, req);
        return result == null
            ? NotFound(APIResponse.Fail("Không tìm thấy môn học.", 404))
            : Ok(APIResponse<SubjectResponse>.Success(result, "Cập nhật môn học thành công."));
    }

    [HttpDelete("subjects/{id:int}")]
    public Task<ActionResult<APIResponse<object>>> DeleteSubject(int id)
        => DeleteGuarded(() => _lookup.DeleteSubjectAsync(id), "môn học");

    // GradeLevels

    [HttpPost("grade-levels")]
    public async Task<ActionResult<APIResponse<GradeLevelResponse>>> CreateGradeLevel(
        [FromBody] GradeLevelRequest req)
    {
        if (!ModelState.IsValid) return BadRequest(APIResponse.Fail("Dữ liệu khối lớp không hợp lệ.", 400));
        var result = await _lookup.CreateGradeLevelAsync(req);
        return Ok(APIResponse<GradeLevelResponse>.Success(result, "Tạo khối lớp thành công."));
    }

    [HttpPut("grade-levels/{id:int}")]
    public async Task<ActionResult<APIResponse<GradeLevelResponse>>> UpdateGradeLevel(
        int id, [FromBody] GradeLevelRequest req)
    {
        if (!ModelState.IsValid) return BadRequest(APIResponse.Fail("Dữ liệu khối lớp không hợp lệ.", 400));
        var result = await _lookup.UpdateGradeLevelAsync(id, req);
        return result == null
            ? NotFound(APIResponse.Fail("Không tìm thấy khối lớp.", 404))
            : Ok(APIResponse<GradeLevelResponse>.Success(result, "Cập nhật khối lớp thành công."));
    }

    [HttpDelete("grade-levels/{id:int}")]
    public Task<ActionResult<APIResponse<object>>> DeleteGradeLevel(int id)
        => DeleteGuarded(() => _lookup.DeleteGradeLevelAsync(id), "khối lớp");

    // Chapters

    [HttpPost("chapters")]
    public async Task<ActionResult<APIResponse<ChapterResponse>>> CreateChapter(
        [FromBody] ChapterRequest req)
    {
        if (!ModelState.IsValid) return BadRequest(APIResponse.Fail("Dữ liệu chương không hợp lệ.", 400));
        var result = await _lookup.CreateChapterAsync(req);
        return Ok(APIResponse<ChapterResponse>.Success(result, "Tạo chương thành công."));
    }

    [HttpPut("chapters/{id:int}")]
    public async Task<ActionResult<APIResponse<ChapterResponse>>> UpdateChapter(
        int id, [FromBody] ChapterRequest req)
    {
        if (!ModelState.IsValid) return BadRequest(APIResponse.Fail("Dữ liệu chương không hợp lệ.", 400));
        var result = await _lookup.UpdateChapterAsync(id, req);
        return result == null
            ? NotFound(APIResponse.Fail("Không tìm thấy chương.", 404))
            : Ok(APIResponse<ChapterResponse>.Success(result, "Cập nhật chương thành công."));
    }

    [HttpDelete("chapters/{id:int}")]
    public Task<ActionResult<APIResponse<object>>> DeleteChapter(int id)
        => DeleteGuarded(() => _lookup.DeleteChapterAsync(id), "chương");

    // QuestionTypes

    [HttpPost("question-types")]
    public async Task<ActionResult<APIResponse<QuestionTypeResponse>>> CreateQuestionType(
        [FromBody] QuestionTypeRequest req)
    {
        if (!ModelState.IsValid) return BadRequest(APIResponse.Fail("Dữ liệu loại câu hỏi không hợp lệ.", 400));
        var result = await _lookup.CreateQuestionTypeAsync(req);
        return Ok(APIResponse<QuestionTypeResponse>.Success(result, "Tạo loại câu hỏi thành công."));
    }

    [HttpPut("question-types/{id:int}")]
    public async Task<ActionResult<APIResponse<QuestionTypeResponse>>> UpdateQuestionType(
        int id, [FromBody] QuestionTypeRequest req)
    {
        if (!ModelState.IsValid) return BadRequest(APIResponse.Fail("Dữ liệu loại câu hỏi không hợp lệ.", 400));
        var result = await _lookup.UpdateQuestionTypeAsync(id, req);
        return result == null
            ? NotFound(APIResponse.Fail("Không tìm thấy loại câu hỏi.", 404))
            : Ok(APIResponse<QuestionTypeResponse>.Success(result, "Cập nhật loại câu hỏi thành công."));
    }

    [HttpDelete("question-types/{id:int}")]
    public Task<ActionResult<APIResponse<object>>> DeleteQuestionType(int id)
        => DeleteGuarded(() => _lookup.DeleteQuestionTypeAsync(id), "loại câu hỏi");

    // Helpers

    /// <summary>Bọc xoá: not-found -> 404, đang tham chiếu -> 409, ok -> 200.</summary>
    private async Task<ActionResult<APIResponse<object>>> DeleteGuarded(
        Func<Task<bool>> deleteFn, string label)
    {
        try
        {
            var ok = await deleteFn();
            return ok
                ? Ok(APIResponse<object>.Success($"Xoá {label} thành công."))
                : NotFound(APIResponse.Fail($"Không tìm thấy {label}.", 404));
        }
        catch (LookupInUseException ex)
        {
            return Conflict(APIResponse.Fail(ex.Message, 409));
        }
    }
}
