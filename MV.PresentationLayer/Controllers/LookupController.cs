using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.DTO.ResponseModel.Question;

namespace MV.PresentationLayer.Controllers
{
    /// <summary>
    /// Dữ liệu tra cứu công khai cho FE (môn học, khối lớp) — đổ dropdown ở Onboarding.
    /// </summary>
    [Route("api")]
    [ApiController]
    [AllowAnonymous]
    public class LookupController : ControllerBase
    {
        private readonly ILookupService _lookupService;

        public LookupController(ILookupService lookupService)
        {
            _lookupService = lookupService;
        }

        /// <summary>
        /// Danh sách môn học.
        /// GET /api/subjects
        /// </summary>
        [HttpGet("subjects")]
        public async Task<IActionResult> GetSubjects()
        {
            try
            {
                var result = await _lookupService.GetSubjectsAsync();
                return Ok(APIResponse<List<SubjectResponse>>.Success(result, "Lấy danh sách môn học thành công."));
            }
            catch (Exception ex)
            {
                return StatusCode(500, APIResponse<object>.Fail(ApiMessages.GenericErrorPrefix + ex.Message, 500));
            }
        }

        /// <summary>
        /// Danh sách khối lớp (sắp theo Levelorder).
        /// GET /api/grade-levels
        /// </summary>
        [HttpGet("grade-levels")]
        public async Task<IActionResult> GetGradeLevels()
        {
            try
            {
                var result = await _lookupService.GetGradeLevelsAsync();
                return Ok(APIResponse<List<GradeLevelResponse>>.Success(result, "Lấy danh sách khối lớp thành công."));
            }
            catch (Exception ex)
            {
                return StatusCode(500, APIResponse<object>.Fail(ApiMessages.GenericErrorPrefix + ex.Message, 500));
            }
        }

        /// <summary>
        /// Danh sách thứ trong tuần (sắp theo DayOrder).
        /// GET /api/days-of-week
        /// </summary>
        [HttpGet("days-of-week")]
        public async Task<IActionResult> GetDaysOfWeek()
        {
            try
            {
                var result = await _lookupService.GetDaysOfWeekAsync();
                return Ok(APIResponse<List<DayOfWeekResponse>>.Success(result, "Lấy danh sách thứ trong tuần thành công."));
            }
            catch (Exception ex)
            {
                return StatusCode(500, APIResponse<object>.Fail(ApiMessages.GenericErrorPrefix + ex.Message, 500));
            }
        }

        /// <summary>
        /// Danh sách chương, lọc theo môn+lớp. GET /api/chapters?subjectId=1&gradeLevelId=60
        /// </summary>
        [HttpGet("chapters")]
        public async Task<IActionResult> GetChapters([FromQuery] int? subjectId, [FromQuery] int? gradeLevelId)
        {
            try
            {
                var result = await _lookupService.GetChaptersAsync(subjectId, gradeLevelId);
                return Ok(APIResponse<List<ChapterResponse>>.Success(result, "Lấy danh sách chương thành công."));
            }
            catch (Exception ex)
            {
                return StatusCode(500, APIResponse<object>.Fail(ApiMessages.GenericErrorPrefix + ex.Message, 500));
            }
        }

        /// <summary>Danh sách loại câu hỏi. GET /api/question-types</summary>
        [HttpGet("question-types")]
        public async Task<IActionResult> GetQuestionTypes()
        {
            try
            {
                var result = await _lookupService.GetQuestionTypesAsync();
                return Ok(APIResponse<List<QuestionTypeResponse>>.Success(result, "Lấy danh sách loại câu hỏi thành công."));
            }
            catch (Exception ex)
            {
                return StatusCode(500, APIResponse<object>.Fail(ApiMessages.GenericErrorPrefix + ex.Message, 500));
            }
        }
    }
}
