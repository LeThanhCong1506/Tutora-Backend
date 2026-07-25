using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.Exceptions;

namespace MV.PresentationLayer.Controllers;

[ApiController]
[Route("api/ai-credit")]
public class AiCreditController(
    IAiCreditService aiCreditService,
    ILogger<AiCreditController> logger) : ControllerBase
{
    private string? UserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    /// <summary>Danh sách gói AI credit đang mở (bảng giá công khai — không cần đăng nhập).</summary>
    [HttpGet("packages")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPackages(CancellationToken ct)
        => Ok(await aiCreditService.GetActivePackagesAsync(ct));

    /// <summary>Số dư AI credit của tài khoản đang đăng nhập.</summary>
    [HttpGet("balance")]
    [Authorize]
    public async Task<IActionResult> GetBalance(CancellationToken ct)
    {
        var userId = UserId;
        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized(new { errorCode = "UNAUTHORIZED", message = "Không xác định được người dùng." });
        try
        {
            return Ok(await aiCreditService.GetBalanceAsync(userId, ct));
        }
        catch (BookingException ex)
        {
            return StatusCode(ex.HttpStatus, new { errorCode = ex.ErrorCode, message = ex.Message });
        }
    }

    /// <summary>Lịch sử cấp/tiêu credit của tài khoản đang đăng nhập.</summary>
    [HttpGet("history")]
    [Authorize]
    public async Task<IActionResult> GetHistory([FromQuery] int take = 50, CancellationToken ct = default)
    {
        var userId = UserId;
        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized(new { errorCode = "UNAUTHORIZED", message = "Không xác định được người dùng." });
        try
        {
            return Ok(await aiCreditService.GetHistoryAsync(userId, take, ct));
        }
        catch (BookingException ex)
        {
            return StatusCode(ex.HttpStatus, new { errorCode = ex.ErrorCode, message = ex.Message });
        }
    }

    /// <summary>Khởi tạo mua gói cho tài khoản đang đăng nhập; trả về link thanh toán PayOS.</summary>
    [HttpPost("purchase")]
    [Authorize]
    public async Task<IActionResult> Purchase([FromBody] AiCreditPurchaseRequest request, CancellationToken ct)
    {
        var buyerUserId = UserId;
        if (string.IsNullOrWhiteSpace(buyerUserId))
            return Unauthorized(new { errorCode = "UNAUTHORIZED", message = "Không xác định được người dùng." });

        try
        {
            return Ok(await aiCreditService.InitiatePurchaseAsync(buyerUserId, request, ct));
        }
        catch (BookingException ex)
        {
            return StatusCode(ex.HttpStatus, new { errorCode = ex.ErrorCode, message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Lỗi khi khởi tạo mua gói AI credit");
            return StatusCode(500, new { errorCode = ApiErrorCodes.InternalError, message = "Lỗi hệ thống khi tạo liên kết mua gói." });
        }
    }
}
