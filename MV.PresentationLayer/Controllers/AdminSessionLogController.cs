using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Helpers;
using MV.PresentationLayer.Authorization;

namespace MV.PresentationLayer.Controllers;

/// <summary>
/// Attendance evidence rebuilt from Agora channel notifications, for staff handling disputes.
///
/// Gated behind <see cref="Permissions.DisputeView"/> rather than a permission of its own: this is
/// dispute-investigation material, and anyone allowed to open a dispute needs it to do the job.
/// </summary>
[ApiController]
[Route("api/admin/class-sessions")]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AdminSessionLogController(ISessionLogService sessionLogService) : ControllerBase
{
    /// <summary>
    /// GET /api/admin/class-sessions/{classSessionId}/session-log
    /// Timeline of who was in the lesson room and when, plus the time both sides were present
    /// together — the figure that decides most attendance disputes.
    /// </summary>
    [HttpGet("{classSessionId:int}/session-log")]
    [RequirePermission(Permissions.DisputeView)]
    public async Task<IActionResult> GetSessionLog(int classSessionId, CancellationToken cancellationToken)
    {
        var log = await sessionLogService.GetSessionLogAsync(classSessionId, cancellationToken);

        if (log == null)
            return NotFound(APIResponse<object>.Fail("Không tìm thấy buổi học.", 404));

        return Ok(APIResponse<SessionLogResponse>.Success(log, "Lấy nhật ký buổi học thành công."));
    }
}

/// <summary>
/// Reliability of a tutor across many lessons, built from the same evidence a single dispute is
/// settled with. Separate route prefix from the per-session log; same permission, because it answers
/// the same kind of question about the same data.
/// </summary>
[ApiController]
[Route("api/admin/tutors")]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AdminTutorReliabilityController(ISessionLogService sessionLogService) : ControllerBase
{
    /// <summary>Longest range one request may span, so a stray query cannot scan the whole history.</summary>
    private const int MaxRangeDays = 400;

    /// <summary>
    /// GET /api/admin/tutors/{tutorUserId}/reliability?from=&amp;to=
    /// Late-arrival, early-departure and no-show rates, plus the per-lesson rows behind them.
    /// Defaults to the last 90 days when no range is given.
    /// </summary>
    [HttpGet("{tutorUserId}/reliability")]
    [RequirePermission(Permissions.DisputeView)]
    public async Task<IActionResult> GetReliability(
        string tutorUserId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tutorUserId))
            return BadRequest(APIResponse<object>.Fail("Thiếu mã gia sư.", 400));

        var toDate = (to ?? TimeZoneHelper.UtcNow).ToUniversalTime();
        var fromDate = (from ?? toDate.AddDays(-90)).ToUniversalTime();

        if (fromDate >= toDate)
            return BadRequest(APIResponse<object>.Fail("Khoảng thời gian không hợp lệ.", 400));

        if ((toDate - fromDate).TotalDays > MaxRangeDays)
        {
            return BadRequest(APIResponse<object>.Fail(
                $"Khoảng thời gian tối đa là {MaxRangeDays} ngày.",
                400));
        }

        var report = await sessionLogService.GetTutorReliabilityAsync(
            tutorUserId,
            fromDate,
            toDate,
            cancellationToken);

        return Ok(APIResponse<TutorReliabilityResponse>.Success(
            report,
            "Lấy thống kê độ tin cậy của gia sư thành công."));
    }
}
