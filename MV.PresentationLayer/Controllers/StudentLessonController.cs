using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.RepositoryInterfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.ResponseModel;
using MV.PresentationLayer.Helpers;
using System.Security.Claims;

namespace MV.PresentationLayer.Controllers
{
    [ApiController]
    [Route("api/student")]
    [Authorize(Roles = UserRole.Student)]
    public class StudentLessonController : ControllerBase
    {
        private readonly ISettlementService _settlementService;
        private readonly ILessonService _lessonService;
        private readonly IStudentRepository _studentRepository;

        public StudentLessonController(
            ISettlementService settlementService,
            ILessonService lessonService,
            IStudentRepository studentRepository)
        {
            _settlementService = settlementService;
            _lessonService = lessonService;
            _studentRepository = studentRepository;
        }

        private string? UserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

        private async Task<string?> GetMyStudentProfileId()
        {
            var profile = await _studentRepository.FindByStudentOrLinkedUserAsync(UserId!);
            return profile?.Studentid;
        }

        // 1. GET /api/student/lessons
        [HttpGet("lessons")]
        public async Task<IActionResult> GetStudentLessons(int page = 1, int pageSize = 10, string? status = null)
        {
            var studentId = await GetMyStudentProfileId();
            if (studentId == null) return NotFound(APIResponse<object>.Fail("Không tìm thấy hồ sơ học sinh.", 404));

            var (items, total) = await _lessonService.GetStudentLessonsAsync(studentId, page, pageSize, status);
            return Ok(APIResponse<object>.Success(new { items, totalCount = total }));
        }

        // 2. GET /api/student/lessons/{id}
        [HttpGet("lessons/{id}")]
        public async Task<IActionResult> GetLessonDetail(int id)
        {
            var studentId = await GetMyStudentProfileId();
            if (studentId == null) return NotFound(APIResponse<object>.Fail("Không tìm thấy hồ sơ học sinh.", 404));

            var lesson = await _lessonService.GetStudentLessonDetailAsync(id, studentId);
            if (lesson == null) return NotFound(APIResponse<object>.Fail(ApiMessages.LessonNotFound, 404));
            return Ok(APIResponse<object>.Success(lesson));
        }

        // 3. GET /api/student/lessons/pending
        [HttpGet("lessons/pending")]
        public async Task<IActionResult> GetPendingLessons()
        {
            var studentId = await GetMyStudentProfileId();
            if (studentId == null) return NotFound(APIResponse<object>.Fail("Không tìm thấy hồ sơ học sinh.", 404));

            var lessons = await _lessonService.GetStudentPendingLessonsAsync(studentId);
            return Ok(APIResponse<object>.Success(lessons));
        }

        // 4. GET /api/student/lessons/calendar
        [HttpGet("lessons/calendar")]
        public async Task<ActionResult<APIResponse<List<CalendarDayResponse>>>> GetCalendar(
            [FromQuery] DateTime? startDate,
            [FromQuery] DateTime? endDate)
        {
            var userId = UserHelper.GetUserId(User);
            var start = startDate ?? MV.DomainLayer.Helpers.VietnamTimeHelper.Now.Date;
            var end = endDate ?? start.AddDays(30);

            var result = await _lessonService.GetStudentCalendarAsync(userId, start, end);
            return Ok(APIResponse<List<CalendarDayResponse>>.Success(result, "Lấy lịch học thành công."));
        }

        // 5. PUT /api/student/lessons/{id}/confirm
        [HttpPut("lessons/{id}/confirm")]
        public async Task<IActionResult> ConfirmLesson(int id)
        {
            var studentId = await GetMyStudentProfileId();
            if (studentId == null) return NotFound(APIResponse<object>.Fail("Không tìm thấy hồ sơ học sinh.", 404));

            var lesson = await _lessonService.GetStudentLessonDetailAsync(id, studentId);
            if (lesson == null) return NotFound(APIResponse<object>.Fail(ApiMessages.LessonNotFound, 404));

            if (lesson.Status != LessonStatus.PendingConfirmation)
                return BadRequest(APIResponse<object>.Fail($"Không thể xác nhận buổi học có trạng thái '{lesson.Status}'. Chỉ buổi học ở trạng thái 'pending_confirmation' mới có thể xác nhận.", 400));

            await _settlementService.SettleLessonAsync(id, UserId ?? "");
            return Ok(APIResponse<object>.Success("Xác nhận buổi học thành công."));
        }
    }
}
