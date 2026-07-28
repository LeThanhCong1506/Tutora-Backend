using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.RepositoryInterfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.PresentationLayer.Helpers;
using System.Security.Claims;

namespace MV.PresentationLayer.Controllers
{
    [ApiController]
    [Route("api/student")]
    [Authorize(Roles = UserRole.Student)]
    public class StudentClassSessionController : ControllerBase
    {
        private readonly ISettlementService _settlementService;
        private readonly IClassSessionService _classSessionService;
        private readonly IStudentRepository _studentRepository;
        private readonly IClassSessionScheduleChangeService _scheduleChangeService;

        public StudentClassSessionController(
            ISettlementService settlementService,
            IClassSessionService classSessionService,
            IStudentRepository studentRepository,
            IClassSessionScheduleChangeService scheduleChangeService)
        {
            _settlementService = settlementService;
            _classSessionService = classSessionService;
            _studentRepository = studentRepository;
            _scheduleChangeService = scheduleChangeService;
        }

        private string? UserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

        private async Task<string?> GetMyStudentProfileId()
        {
            var profile = await _studentRepository.FindByStudentOrLinkedUserAsync(UserId!);
            return profile?.Studentid;
        }

        // 1. GET /api/student/classSessions
        [HttpGet("class-sessions")]
        public async Task<IActionResult> GetStudentClassSessions(int page = 1, int pageSize = 10, string? status = null)
        {
            var studentId = await GetMyStudentProfileId();
            if (studentId == null) return NotFound(APIResponse<object>.Fail("Không tìm thấy hồ sơ học sinh.", 404));

            var (items, total) = await _classSessionService.GetStudentClassSessionsAsync(studentId, page, pageSize, status);
            return Ok(APIResponse<object>.Success(new { items, totalCount = total }));
        }

        // 2. GET /api/student/classSessions/{id}
        [HttpGet("class-sessions/{id}")]
        public async Task<IActionResult> GetClassSessionDetail(int id)
        {
            var studentId = await GetMyStudentProfileId();
            if (studentId == null) return NotFound(APIResponse<object>.Fail("Không tìm thấy hồ sơ học sinh.", 404));

            var classSession = await _classSessionService.GetStudentClassSessionDetailAsync(id, studentId);
            if (classSession == null) return NotFound(APIResponse<object>.Fail(ApiMessages.ClassSessionNotFound, 404));
            return Ok(APIResponse<object>.Success(classSession));
        }

        // 3. GET /api/student/classSessions/pending
        [HttpGet("class-sessions/pending")]
        public async Task<IActionResult> GetPendingClassSessions()
        {
            var studentId = await GetMyStudentProfileId();
            if (studentId == null) return NotFound(APIResponse<object>.Fail("Không tìm thấy hồ sơ học sinh.", 404));

            var classSessions = await _classSessionService.GetStudentPendingClassSessionsAsync(studentId);
            return Ok(APIResponse<object>.Success(classSessions));
        }

        // 4. GET /api/student/classSessions/calendar
        [HttpGet("class-sessions/calendar")]
        public async Task<ActionResult<APIResponse<List<CalendarDayResponse>>>> GetCalendar(
            [FromQuery] DateTime? startDate,
            [FromQuery] DateTime? endDate)
        {
            var userId = UserHelper.GetUserId(User);
            var start = startDate ?? MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow.Date;
            var end = endDate ?? start.AddDays(30);

            var result = await _classSessionService.GetStudentCalendarAsync(userId, start, end);
            return Ok(APIResponse<List<CalendarDayResponse>>.Success(result, "Lấy lịch học thành công."));
        }

        // 5. PUT /api/student/classSessions/{id}/confirm
        [HttpPut("class-sessions/{id}/confirm")]
        public async Task<IActionResult> ConfirmClassSession(int id)
        {
            var studentId = await GetMyStudentProfileId();
            if (studentId == null) return NotFound(APIResponse<object>.Fail("Không tìm thấy hồ sơ học sinh.", 404));

            var classSession = await _classSessionService.GetStudentClassSessionDetailAsync(id, studentId);
            if (classSession == null) return NotFound(APIResponse<object>.Fail(ApiMessages.ClassSessionNotFound, 404));

            if (classSession.Status != ClassSessionStatus.PendingConfirmation)
                return BadRequest(APIResponse<object>.Fail($"Không thể xác nhận buổi học có trạng thái '{classSession.Status}'. Chỉ buổi học ở trạng thái 'pending_confirmation' mới có thể xác nhận.", 400));

            await _settlementService.SettleClassSessionAsync(id, UserId ?? "");
            return Ok(APIResponse<object>.Success("Xác nhận buổi học thành công."));
        }

        /// <summary>
        /// Học sinh tự quản lý (>16, không có phụ huynh) đọc yêu cầu xác nhận đổi lịch (ngoài giờ đã
        /// đặt) đã được tạo cho buổi học này, xem từ trang chi tiết buổi học — không tạo request mới.
        /// </summary>
        [HttpGet("class-sessions/{id}/schedule-change")]
        public async Task<ActionResult<APIResponse<SessionScheduleChangeResponse>>> GetScheduleChange(int id)
        {
            var userId = UserHelper.GetUserId(User);
            try
            {
                var result = await _scheduleChangeService.GetExistingStateAsync(id, userId, UserRole.Student);
                return Ok(APIResponse<SessionScheduleChangeResponse>.Success(result, "Lấy trạng thái xác nhận đổi lịch thành công."));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(APIResponse<object>.Fail(ex.Message, 404));
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, APIResponse<object>.Fail(ex.Message, 403));
            }
        }

        /// <summary>
        /// Học sinh tự quản lý xác nhận/từ chối yêu cầu đổi lịch đang chờ, không cần đang ở trong
        /// phòng chờ. Nếu học sinh này thuộc phụ huynh quản lý, service sẽ từ chối vì người có quyền
        /// xác nhận là phụ huynh, không phải học sinh.
        /// </summary>
        [HttpPost("class-sessions/{id}/schedule-change/respond")]
        public async Task<ActionResult<APIResponse<SessionScheduleChangeResponse>>> RespondToScheduleChange(
            int id,
            [FromBody] SessionScheduleChangeDecisionRequest request)
        {
            var userId = UserHelper.GetUserId(User);
            try
            {
                var existing = await _scheduleChangeService.GetExistingStateAsync(id, userId, UserRole.Student);
                if (!existing.RequiresConfirmation || existing.Status != ScheduleChangeStatus.Pending)
                    return BadRequest(APIResponse<object>.Fail("Yêu cầu đổi lịch không còn chờ xác nhận.", 400));

                var result = await _scheduleChangeService.RespondAsync(id, userId, UserRole.Student, request.Confirmed);
                return Ok(APIResponse<SessionScheduleChangeResponse>.Success(
                    result,
                    request.Confirmed ? "Đã xác nhận đổi lịch học." : "Đã từ chối đổi lịch học."));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(APIResponse<object>.Fail(ex.Message, 404));
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, APIResponse<object>.Fail(ex.Message, 403));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(APIResponse<object>.Fail(ex.Message, 400));
            }
        }
    }
}
