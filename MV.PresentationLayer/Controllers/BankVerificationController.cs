using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MV.ApplicationLayer.Interfaces;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.BankVerification;
using MV.DomainLayer.Exceptions;
using MV.DomainLayer.Settings;
using System.Security.Claims;
using MV.DomainLayer.Constants;

namespace MV.PresentationLayer.Controllers;

[ApiController]
[Route("api/tutor/bank-verification")]
[Authorize(Roles = UserRole.Tutor)]
public class BankVerificationController(
    IBankVerificationService bankVerificationService,
    IOptions<BankVerificationSettings> bankVerificationSettings,
    ILogger<BankVerificationController> logger) : ControllerBase
{
    private readonly BankVerificationSettings _settings = bankVerificationSettings.Value;

    private string UserId
    {
        get
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userIdClaim))
            {
                logger.LogWarning("Invalid or missing UserId claim: {Claim}", userIdClaim);
                throw new UnauthorizedAccessException("Không xác định được thông tin người dùng.");
            }
            return userIdClaim;
        }
    }

    [HttpPost("request")]
    public async Task<ActionResult<APIResponse<RequestVerifyResponse>>> RequestVerification(
        [FromBody] RequestVerifyRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await bankVerificationService.RequestVerificationAsync(UserId, request, cancellationToken);

            return result.IsSuccess
                ? Ok(APIResponse<RequestVerifyResponse>.Success(result, "Yêu cầu xác thực đã được gửi. Vui lòng kiểm tra tài khoản ngân hàng của bạn."))
                : BadRequest(APIResponse<RequestVerifyResponse>.Fail(result.ErrorMessage ?? "Yêu cầu xác thực thất bại.", 400, new { result.ErrorCode }));
        }
        catch (InvalidBankInfoException ex)
        {
            return BadRequest(APIResponse<RequestVerifyResponse>.Fail(ex.Message, 400, new { ex.ErrorCode }));
        }
        catch (AlreadyVerifiedException ex)
        {
            return BadRequest(APIResponse<RequestVerifyResponse>.Fail(ex.Message, 400, new { ErrorCode = BankVerificationConstants.ErrorCodes.AlreadyVerified }));
        }
        catch (RateLimitExceededException ex)
        {
            return StatusCode(429, APIResponse<RequestVerifyResponse>.Fail(ex.Message, 429, new { ErrorCode = BankVerificationConstants.ErrorCodes.RateLimitExceeded }));
        }
        catch (BookingException ex) when (ex.ErrorCode == WalletErrorCodes.InsufficientBalanceForVerification)
        {
            return BadRequest(APIResponse<RequestVerifyResponse>.Fail(ex.Message, 400, new { ex.ErrorCode }));
        }
        catch (NotFoundException ex)
        {
            return NotFound(APIResponse<RequestVerifyResponse>.Fail(ex.Message, 404));
        }
        catch
        {
            return StatusCode(500, APIResponse<RequestVerifyResponse>.Fail("Đã xảy ra lỗi khi gửi yêu cầu xác thực.", 500, new { ErrorCode = ApiErrorCodes.InternalError }));
        }
    }

    [HttpPost("confirm")]
    public async Task<ActionResult<APIResponse<ConfirmVerifyResponse>>> ConfirmVerification(
        [FromBody] ConfirmVerifyRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await bankVerificationService.ConfirmVerificationAsync(UserId, request, cancellationToken);

            return result.IsVerified
                ? Ok(APIResponse<ConfirmVerifyResponse>.Success(result, result.Message ?? "Xác thực tài khoản ngân hàng thành công."))
                : BadRequest(APIResponse<ConfirmVerifyResponse>.Fail(result.ErrorMessage ?? "Mã xác thực không đúng.", 400, result));
        }
        catch (VerificationExpiredException ex)
        {
            return BadRequest(APIResponse<ConfirmVerifyResponse>.Fail(ex.Message, 400, new { ErrorCode = BankVerificationConstants.ErrorCodes.VerificationExpired }));
        }
        catch (BankVerificationException ex)
        {
            return BadRequest(APIResponse<ConfirmVerifyResponse>.Fail(ex.Message, 400, new { ex.ErrorCode }));
        }
        catch (NotFoundException ex)
        {
            return NotFound(APIResponse<ConfirmVerifyResponse>.Fail(ex.Message, 404));
        }
        catch
        {
            return StatusCode(500, APIResponse<ConfirmVerifyResponse>.Fail("Đã xảy ra lỗi khi xác thực tài khoản ngân hàng.", 500, new { ErrorCode = ApiErrorCodes.InternalError }));
        }
    }

    [HttpGet("status")]
    public async Task<ActionResult<APIResponse<BankVerificationStatusResponse>>> GetVerificationStatus(
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await bankVerificationService.GetVerificationStatusAsync(UserId, cancellationToken);

            return Ok(APIResponse<BankVerificationStatusResponse>.Success(result, "Lấy trạng thái xác thực thành công."));
        }
        catch (NotFoundException ex)
        {
            return NotFound(APIResponse<BankVerificationStatusResponse>.Fail(ex.Message, 404));
        }
        catch
        {
            return StatusCode(500, APIResponse<BankVerificationStatusResponse>.Fail("Đã xảy ra lỗi khi lấy trạng thái xác thực.", 500, new { ErrorCode = ApiErrorCodes.InternalError }));
        }
    }
}
