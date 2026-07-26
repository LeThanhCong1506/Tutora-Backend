using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel.Question;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.DTO.ResponseModel.Question;
using MV.PresentationLayer.Authorization;

namespace MV.PresentationLayer.Controllers;

/// <summary>
/// CRUD danh mục (môn học / khối lớp / chương / loại câu hỏi) cho CMS admin.
/// </summary>
[ApiController]
[Route("api/admin/lookup")]
[Authorize]
public class AdminLookupController : ControllerBase
{
    private const string SubjectIconBucket = "subject-icons";
    private const long MaxIconSizeBytes = 2 * 1024 * 1024;

    private readonly ILookupService _lookup;
    private readonly IFileStorageService _storage;

    public AdminLookupController(ILookupService lookup, IFileStorageService storage)
    {
        _lookup = lookup;
        _storage = storage;
    }

    /// <summary>
    /// Upload icon môn học, trả về public URL.
    /// </summary>
    [HttpPost("subjects/icon")]
    [RequestSizeLimit(MaxIconSizeBytes)]
    [RequirePermission(Permissions.LookupCreate)]
    public async Task<ActionResult<APIResponse<object>>> UploadSubjectIcon(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(APIResponse.Fail("Chưa chọn file.", 400));

        if (file.Length > MaxIconSizeBytes)
            return BadRequest(APIResponse.Fail("Kích thước ảnh vượt quá 2MB.", 400));

        if (!file.ContentType.StartsWith("image/"))
            return BadRequest(APIResponse.Fail("Chỉ chấp nhận file hình ảnh.", 400));

        await _storage.EnsureBucketExistsAsync(SubjectIconBucket);
        var url = await _storage.UploadFileAsync(SubjectIconBucket, "subjects", file);
        return Ok(APIResponse<object>.Success(new { iconUrl = url }, "Tải icon lên thành công."));
    }

    // Subjects
    /// <summary>Liệt kê tất cả môn học (gồm cả đã ngừng dùng). GET /api/admin/lookup/subjects</summary>
    [HttpGet("subjects")]
    [RequirePermission(Permissions.LookupView)]
    public async Task<ActionResult<APIResponse<List<SubjectResponse>>>> GetSubjects()
    {
        var result = await _lookup.GetAllSubjectsAsync();
        return Ok(APIResponse<List<SubjectResponse>>.Success(result, "Lấy danh sách môn học thành công."));
    }

    [HttpPost("subjects")]
    [RequirePermission(Permissions.LookupCreate)]
    public async Task<ActionResult<APIResponse<SubjectResponse>>> CreateSubject(
        [FromBody] SubjectRequest req)
    {
        if (!ModelState.IsValid) return BadRequest(APIResponse.Fail("Dữ liệu môn học không hợp lệ.", 400));
        var result = await _lookup.CreateSubjectAsync(req);
        return Ok(APIResponse<SubjectResponse>.Success(result, "Tạo môn học mới thành công."));
    }

    [HttpPut("subjects/{id:int}")]
    [RequirePermission(Permissions.LookupUpdate)]
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
    [RequirePermission(Permissions.LookupDelete)]
    public Task<ActionResult<APIResponse<object>>> DeleteSubject(int id)
        => DeleteGuarded(() => _lookup.DeleteSubjectAsync(id), "môn học");

    // GradeLevels

    /// <summary>Liệt kê tất cả khối lớp (gồm cả đã ngừng dùng). GET /api/admin/lookup/grade-levels</summary>
    [HttpGet("grade-levels")]
    [RequirePermission(Permissions.LookupView)]
    public async Task<ActionResult<APIResponse<List<GradeLevelResponse>>>> GetGradeLevels()
    {
        var result = await _lookup.GetAllGradeLevelsAsync();
        return Ok(APIResponse<List<GradeLevelResponse>>.Success(result, "Lấy danh sách khối lớp thành công."));
    }

    [HttpPost("grade-levels")]
    [RequirePermission(Permissions.LookupCreate)]
    public async Task<ActionResult<APIResponse<GradeLevelResponse>>> CreateGradeLevel(
        [FromBody] GradeLevelRequest req)
    {
        if (!ModelState.IsValid) return BadRequest(APIResponse.Fail("Dữ liệu khối lớp không hợp lệ.", 400));
        var result = await _lookup.CreateGradeLevelAsync(req);
        return Ok(APIResponse<GradeLevelResponse>.Success(result, "Tạo khối lớp thành công."));
    }

    [HttpPut("grade-levels/{id:int}")]
    [RequirePermission(Permissions.LookupUpdate)]
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
    [RequirePermission(Permissions.LookupDelete)]
    public Task<ActionResult<APIResponse<object>>> DeleteGradeLevel(int id)
        => DeleteGuarded(() => _lookup.DeleteGradeLevelAsync(id), "khối lớp");

    // Chapters

    /// <summary>Liệt kê chương (gồm cả đã ngừng dùng), lọc theo môn/khối.
    [HttpGet("chapters")]
    [RequirePermission(Permissions.LookupView)]
    public async Task<ActionResult<APIResponse<List<AdminChapterResponse>>>> GetChapters(
        [FromQuery] int? subjectId, [FromQuery] int? gradeLevelId)
    {
        var result = await _lookup.GetAllChaptersAsync(subjectId, gradeLevelId);
        return Ok(APIResponse<List<AdminChapterResponse>>.Success(result, "Lấy danh sách chương thành công."));
    }

    [HttpPost("chapters")]
    [RequirePermission(Permissions.LookupCreate)]
    public async Task<ActionResult<APIResponse<ChapterResponse>>> CreateChapter(
        [FromBody] ChapterRequest req)
    {
        if (!ModelState.IsValid) return BadRequest(APIResponse.Fail("Dữ liệu chương không hợp lệ.", 400));
        var result = await _lookup.CreateChapterAsync(req);
        return Ok(APIResponse<ChapterResponse>.Success(result, "Tạo chương thành công."));
    }

    [HttpPut("chapters/{id:int}")]
    [RequirePermission(Permissions.LookupUpdate)]
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
    [RequirePermission(Permissions.LookupDelete)]
    public Task<ActionResult<APIResponse<object>>> DeleteChapter(int id)
        => DeleteGuarded(() => _lookup.DeleteChapterAsync(id), "chương");

    // QuestionTypes

    /// <summary>Liệt kê loại câu hỏi (gồm cả đã ngừng dùng). GET /api/admin/lookup/question-types</summary>
    [HttpGet("question-types")]
    [RequirePermission(Permissions.LookupView)]
    public async Task<ActionResult<APIResponse<List<AdminQuestionTypeResponse>>>> GetQuestionTypes()
    {
        var result = await _lookup.GetAllQuestionTypesAsync();
        return Ok(APIResponse<List<AdminQuestionTypeResponse>>.Success(result, "Lấy danh sách loại câu hỏi thành công."));
    }

    [HttpPost("question-types")]
    [RequirePermission(Permissions.LookupCreate)]
    public async Task<ActionResult<APIResponse<QuestionTypeResponse>>> CreateQuestionType(
        [FromBody] QuestionTypeRequest req)
    {
        if (!ModelState.IsValid) return BadRequest(APIResponse.Fail("Dữ liệu loại câu hỏi không hợp lệ.", 400));
        var result = await _lookup.CreateQuestionTypeAsync(req);
        return Ok(APIResponse<QuestionTypeResponse>.Success(result, "Tạo loại câu hỏi thành công."));
    }

    [HttpPut("question-types/{id:int}")]
    [RequirePermission(Permissions.LookupUpdate)]
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
    [RequirePermission(Permissions.LookupDelete)]
    public Task<ActionResult<APIResponse<object>>> DeleteQuestionType(int id)
        => DeleteGuarded(() => _lookup.DeleteQuestionTypeAsync(id), "loại câu hỏi");

    // Reorder — kéo-thả đổi thứ tự hiển thị trên CMS.
    [HttpPut("subjects/reorder")]
    [RequirePermission(Permissions.LookupUpdate)]
    public Task<ActionResult<APIResponse<object>>> ReorderSubjects([FromBody] ReorderRequest req)
        => ReorderGuarded(() => _lookup.ReorderSubjectsAsync(req), "môn học");

    [HttpPut("grade-levels/reorder")]
    [RequirePermission(Permissions.LookupUpdate)]
    public Task<ActionResult<APIResponse<object>>> ReorderGradeLevels([FromBody] ReorderRequest req)
        => ReorderGuarded(() => _lookup.ReorderGradeLevelsAsync(req), "khối lớp");

    [HttpPut("chapters/reorder")]
    [RequirePermission(Permissions.LookupUpdate)]
    public Task<ActionResult<APIResponse<object>>> ReorderChapters([FromBody] ReorderRequest req)
        => ReorderGuarded(() => _lookup.ReorderChaptersAsync(req), "chương");

    [HttpPut("question-types/reorder")]
    [RequirePermission(Permissions.LookupUpdate)]
    public Task<ActionResult<APIResponse<object>>> ReorderQuestionTypes([FromBody] ReorderRequest req)
        => ReorderGuarded(() => _lookup.ReorderQuestionTypesAsync(req), "loại câu hỏi");

    // Helpers

    /// <summary>Bọc reorder: dữ liệu rỗng/id lạ -> 400, ok -> 200.</summary>
    private async Task<ActionResult<APIResponse<object>>> ReorderGuarded(
        Func<Task<bool>> reorderFn, string label)
    {
        if (!ModelState.IsValid)
            return BadRequest(APIResponse.Fail("Dữ liệu sắp xếp không hợp lệ.", 400));

        var ok = await reorderFn();
        return ok
            ? Ok(APIResponse<object>.Success($"Đã cập nhật thứ tự {label}."))
            : BadRequest(APIResponse.Fail($"Danh sách {label} chứa mục không tồn tại.", 400));
    }

    /// <summary>Bọc xoá: not-found -> 404, đang tham chiếu -> 409, ok -> 200.
    /// Truyền successMessage cho trường hợp soft-delete.</summary>
    private async Task<ActionResult<APIResponse<object>>> DeleteGuarded(
        Func<Task<bool>> deleteFn, string label, string? successMessage = null)
    {
        try
        {
            var ok = await deleteFn();
            return ok
                ? Ok(APIResponse<object>.Success(successMessage ?? $"Xoá {label} thành công."))
                : NotFound(APIResponse.Fail($"Không tìm thấy {label}.", 404));
        }
        catch (LookupInUseException ex)
        {
            return Conflict(APIResponse.Fail(ex.Message, 409));
        }
    }

    /// <summary>Bọc soft-delete Subject/GradeLevel: not-found -> 404, ok -> 200 kèm cảnh báo
    /// số gói giá của tutor đang tham chiếu (không chặn, không cascade — booking/lớp đang dạy
    /// vẫn tiếp tục hoạt động bình thường).</summary>
    private async Task<ActionResult<APIResponse<object>>> DeleteGuarded(
        Func<Task<LookupDeleteResult>> deleteFn, string label)
    {
        var result = await deleteFn();
        if (!result.Found)
            return NotFound(APIResponse.Fail($"Không tìm thấy {label}.", 404));

        var message = result.AffectedCount > 0
            ? $"Đã ngừng sử dụng {label} (đang có {result.AffectedCount} gói giá của tutor tham chiếu — các booking/lớp đang dạy không bị ảnh hưởng)."
            : $"Soft delete {label} thành công (đã ngừng sử dụng).";
        return Ok(APIResponse<object>.Success(message));
    }
}
