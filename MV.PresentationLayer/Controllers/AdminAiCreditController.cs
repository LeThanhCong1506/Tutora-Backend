using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.Exceptions;
using MV.PresentationLayer.Authorization;

namespace MV.PresentationLayer.Controllers;

/// <summary>
/// Admin quản lý gói AI credit (Free/Plus/Pro) và tham số bonus/booking.
/// </summary>
[ApiController]
[Route("api/admin/ai-credit")]
[Authorize]
public class AdminAiCreditController(
    IAiCreditService aiCreditService,
    ILogger<AdminAiCreditController> logger) : ControllerBase
{
    private string? UserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    private IActionResult Handle(BookingException ex)
        => StatusCode(ex.HttpStatus, new { errorCode = ex.ErrorCode, message = ex.Message });

    // Packages
    [HttpGet("packages")]
    [RequirePermission(Permissions.FinancialView)]
    public async Task<IActionResult> GetPackages(CancellationToken ct)
        => Ok(await aiCreditService.AdminGetPackagesAsync(ct));

    [HttpPost("packages")]
    [RequirePermission(Permissions.PromotionManage)]
    public async Task<IActionResult> CreatePackage([FromBody] AiCreditPackageCreateRequest request, CancellationToken ct)
    {
        try { return Ok(await aiCreditService.AdminCreatePackageAsync(request, ct)); }
        catch (BookingException ex) { return Handle(ex); }
    }

    [HttpPut("packages/{packageId:int}")]
    [RequirePermission(Permissions.PromotionManage)]
    public async Task<IActionResult> UpdatePackage(
        [FromRoute] int packageId, [FromBody] AiCreditPackageUpdateRequest request, CancellationToken ct)
    {
        try { return Ok(await aiCreditService.AdminUpdatePackageAsync(packageId, request, ct)); }
        catch (BookingException ex) { return Handle(ex); }
    }

    [HttpDelete("packages/{packageId:int}")]
    [RequirePermission(Permissions.PromotionManage)]
    public async Task<IActionResult> DeletePackage([FromRoute] int packageId, CancellationToken ct)
    {
        try
        {
            await aiCreditService.AdminDeletePackageAsync(packageId, ct);
            return Ok(new { message = "Đã xóa gói." });
        }
        catch (BookingException ex) { return Handle(ex); }
    }

    [HttpPost("packages/{packageId:int}/icon")]
    [RequirePermission(Permissions.PromotionManage)]
    public async Task<IActionResult> UploadIcon([FromRoute] int packageId, IFormFile file, CancellationToken ct)
    {
        try { return Ok(await aiCreditService.AdminUploadIconAsync(packageId, file, ct)); }
        catch (BookingException ex) { return Handle(ex); }
    }

    // Hạn dùng credit + số lượt tặng khi đăng ký
    [HttpGet("config/credit-rules")]
    [RequirePermission(Permissions.FinancialView)]
    public async Task<IActionResult> GetCreditRules(CancellationToken ct)
        => Ok(new
        {
            expiryMonths = await aiCreditService.GetExpiryMonthsAsync(ct),
            freeOnSignup = await aiCreditService.AdminGetFreeOnSignupAsync(ct),
            bookingBonus = await aiCreditService.AdminGetBookingBonusAsync(ct),
        });

    [HttpPut("config/expiry-months")]
    [RequirePermission(Permissions.PromotionManage)]
    public async Task<IActionResult> SetExpiryMonths([FromBody] SetBookingBonusRequest request, CancellationToken ct)
    {
        try
        {
            await aiCreditService.AdminSetExpiryMonthsAsync(request.Amount, UserId, ct);
            return Ok(new { expiryMonths = request.Amount });
        }
        catch (BookingException ex) { return Handle(ex); }
    }

    [HttpPut("config/free-on-signup")]
    [RequirePermission(Permissions.PromotionManage)]
    public async Task<IActionResult> SetFreeOnSignup([FromBody] SetBookingBonusRequest request, CancellationToken ct)
    {
        try
        {
            await aiCreditService.AdminSetFreeOnSignupAsync(request.Amount, UserId, ct);
            return Ok(new { freeOnSignup = request.Amount });
        }
        catch (BookingException ex) { return Handle(ex); }
    }

    // Booking bonus config
    [HttpGet("config/booking-bonus")]
    [RequirePermission(Permissions.FinancialView)]
    public async Task<IActionResult> GetBookingBonus(CancellationToken ct)
        => Ok(new { amount = await aiCreditService.AdminGetBookingBonusAsync(ct) });

    [HttpPut("config/booking-bonus")]
    [RequirePermission(Permissions.PromotionManage)]
    public async Task<IActionResult> SetBookingBonus([FromBody] SetBookingBonusRequest request, CancellationToken ct)
    {
        try
        {
            await aiCreditService.AdminSetBookingBonusAsync(request.Amount, UserId, ct);
            return Ok(new { amount = request.Amount });
        }
        catch (BookingException ex) { return Handle(ex); }
    }
}

/// <summary>Đặt số lượt bonus AI credit mỗi booking.</summary>
public class SetBookingBonusRequest
{
    public int Amount { get; set; }
}
