using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.Exceptions;
using MV.DomainLayer.Settings;
using MV.PresentationLayer.Authorization;

namespace MV.PresentationLayer.Controllers;

/// <summary>
/// Chi phí gọi Gemini: tutora-ai ghi vào (POST events), admin đọc thống kê ra.
/// Google không có API đọc usage theo API key nên đây là nguồn số liệu duy nhất.
/// </summary>
[ApiController]
[Route("api/admin/ai-usage")]
[Authorize]
public class AdminAiUsageController(
    IAdminAiUsageService aiUsageService,
    IConfiguration config,
    ILogger<AdminAiUsageController> logger) : ControllerBase
{
    private string? UserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    private IActionResult Handle(BookingException ex)
        => StatusCode(ex.HttpStatus, new { errorCode = ex.ErrorCode, message = ex.Message });

    /// <summary>
    /// tutora-ai đẩy lô sự kiện về. Không dùng JWT (gọi máy-máy) mà dùng chính
    /// khoá TutorAi:ApiKey hai bên đã chia sẻ sẵn, gửi qua header X-API-Key.
    /// </summary>
    [HttpPost("events")]
    [AllowAnonymous]
    public async Task<IActionResult> Ingest([FromBody] AiUsageIngestRequest request, CancellationToken ct)
    {
        var expected = config[$"{TutorAiSettings.SectionName}:ApiKey"];
        if (string.IsNullOrEmpty(expected))
        {
            // Chưa cấu hình khoá thì KHÔNG mở cửa cho mọi người ghi.
            logger.LogWarning("TutorAi:ApiKey chưa cấu hình — từ chối ghi ai usage.");
            return Unauthorized();
        }

        if (!Request.Headers.TryGetValue("X-API-Key", out var provided) || provided != expected)
            return Unauthorized();

        var saved = await aiUsageService.IngestAsync(request, ct);
        return Ok(new { saved });
    }

    /// <summary>Thống kê cho trang admin: tổng kỳ, chuỗi ngày, gom theo model/feature.</summary>
    [HttpGet]
    [RequirePermission(Permissions.FinancialView)]
    public async Task<IActionResult> GetUsage(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
        => Ok(await aiUsageService.GetUsageAsync(from, to, ct));

    /// <summary>Tỉ giá USD→VND đang dùng để quy đổi chi phí (Google tính bằng USD).</summary>
    [HttpGet("rate")]
    [RequirePermission(Permissions.FinancialView)]
    public async Task<IActionResult> GetRate(CancellationToken ct)
        => Ok(await aiUsageService.GetRateAsync(ct));

    /// <summary>Admin đặt tỉ giá thủ công; rate bỏ trống = quay lại lấy tự động.</summary>
    [HttpPut("rate")]
    [RequirePermission(Permissions.PromotionManage)]
    public async Task<IActionResult> SetRate([FromBody] SetAiUsageRateRequest request, CancellationToken ct)
    {
        try { return Ok(await aiUsageService.SetRateAsync(request.Rate, UserId, ct)); }
        catch (BookingException ex) { return Handle(ex); }
    }
}

/// <summary>Đặt tỉ giá USD→VND. Null = xoá giá trị tay, dùng tỉ giá thị trường.</summary>
public class SetAiUsageRateRequest
{
    public decimal? Rate { get; set; }
}
