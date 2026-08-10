using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.Exceptions;
using System.Security.Claims;

namespace MV.PresentationLayer.Controllers;

/// <summary>
/// Tài khoản ngân hàng dùng chung cho Tutor/Parent/Student tự đăng ký. Mọi thao tác lưu/xoá bắt
/// buộc xác thực OTP trước (gửi tới SĐT riêng đã xác thực của chính người dùng qua Zalo ZNS) —
/// xem <see cref="IBankAccountOtpService"/>/<see cref="IBankAccountService"/>.
/// </summary>
[ApiController]
[Route("api/bank-account")]
[Authorize(Roles = UserRole.ParentOrStudentOrTutor)]
public class BankAccountController(IBankAccountService bankAccountService) : ControllerBase
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    /// <summary>
    /// Địa chỉ IP của client cho audit log. Đơn giản hơn cổng dùng cho bằng chứng gian lận buổi
    /// học (không đọc X-Forwarded-For) — audit log này chỉ phục vụ tra cứu nội bộ khi có
    /// khiếu nại, không phải bằng chứng pháp lý cần chống giả mạo header.
    /// </summary>
    private string? ClientIpAddress => HttpContext.Connection.RemoteIpAddress?.ToString();

    private void SetNoCacheResponseHeaders()
    {
        Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
        Response.Headers.Pragma = "no-cache";
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        SetNoCacheResponseHeaders();
        var result = await bankAccountService.GetAsync(UserId, ct);
        return Ok(APIResponse<object>.Success(result));
    }

    [HttpGet("history")]
    public async Task<IActionResult> GetHistory(CancellationToken ct)
    {
        SetNoCacheResponseHeaders();
        var result = await bankAccountService.GetHistoryAsync(UserId, ct);
        return Ok(APIResponse<object>.Success(result));
    }

    [HttpPost("otp/send")]
    [EnableRateLimiting("otp")]
    public async Task<IActionResult> SendOtp(CancellationToken ct)
    {
        try
        {
            await bankAccountService.SendOtpAsync(UserId, ct);
            return Ok(APIResponse<object>.Success(new { }, "Đã gửi mã OTP tới số điện thoại của bạn."));
        }
        catch (BankAccountOtpPhoneNotVerifiedException ex)
        {
            return BadRequest(new { errorCode = "PHONE_NOT_VERIFIED", message = ex.Message });
        }
        catch (BankAccountNotEligibleException ex)
        {
            return StatusCode(403, new { errorCode = "BANK_ACCOUNT_NOT_ELIGIBLE", message = ex.Message });
        }
        catch (BankAccountOtpCooldownActiveException ex)
        {
            return BadRequest(new { errorCode = "OTP_COOLDOWN_ACTIVE", message = ex.Message, retryAfterSeconds = ex.RetryAfterSeconds });
        }
        catch (BankAccountOtpDailyLimitExceededException ex)
        {
            return BadRequest(new { errorCode = "OTP_DAILY_LIMIT_EXCEEDED", message = ex.Message });
        }
        catch (NotFoundException ex)
        {
            return NotFound(APIResponse.Fail(ex.Message, 404));
        }
    }

    [HttpPost("otp/verify")]
    [EnableRateLimiting("otp")]
    public async Task<IActionResult> VerifyOtp([FromBody] VerifyBankAccountOtpRequest request, CancellationToken ct)
    {
        try
        {
            await bankAccountService.VerifyOtpAsync(UserId, request.Code, ct);
            return Ok(APIResponse<object>.Success(new { }, "Xác thực OTP thành công."));
        }
        catch (BankAccountOtpExpiredException ex)
        {
            return BadRequest(new { errorCode = "OTP_EXPIRED", message = ex.Message });
        }
        catch (BankAccountOtpInvalidException ex)
        {
            return BadRequest(new { errorCode = "OTP_INVALID", message = ex.Message });
        }
        catch (BankAccountOtpTooManyAttemptsException ex)
        {
            return BadRequest(new { errorCode = "OTP_TOO_MANY_ATTEMPTS", message = ex.Message });
        }
    }

    [HttpPut]
    public async Task<IActionResult> Save([FromBody] SaveBankAccountRequest request, CancellationToken ct)
    {
        try
        {
            var result = await bankAccountService.SaveAsync(
                UserId, request, ClientIpAddress, Request.Headers.UserAgent.ToString(), ct);
            return Ok(APIResponse<object>.Success(result, "Cập nhật thông tin ngân hàng thành công."));
        }
        catch (BankAccountOtpRequiredException ex)
        {
            return BadRequest(new { errorCode = "BANK_ACCOUNT_OTP_REQUIRED", message = ex.Message });
        }
        catch (NotFoundException ex)
        {
            return NotFound(APIResponse.Fail(ex.Message, 404));
        }
    }

    [HttpDelete]
    public async Task<IActionResult> Delete(CancellationToken ct)
    {
        try
        {
            await bankAccountService.DeleteAsync(UserId, ClientIpAddress, Request.Headers.UserAgent.ToString(), ct);
            return Ok(APIResponse.Success("Xóa tài khoản ngân hàng thành công."));
        }
        catch (BankAccountOtpRequiredException ex)
        {
            return BadRequest(new { errorCode = "BANK_ACCOUNT_OTP_REQUIRED", message = ex.Message });
        }
        catch (NotFoundException ex)
        {
            return NotFound(APIResponse.Fail(ex.Message, 404));
        }
    }
}
