using MV.DomainLayer.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Exceptions;
using System.Security.Claims;

namespace MV.PresentationLayer.Controllers;

[ApiController]
[Route("api/wallet")]
public class WalletController(IWalletService walletService) : ControllerBase
{
    private string? UserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    [HttpGet("balance")]
    [Authorize(Roles = UserRole.ParentOrStudentOrTutor)]
    public async Task<IActionResult> GetBalance()
    {
        var userId = UserId;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));

        try
        {
            var result = await walletService.GetWalletBalanceAsync(userId);
            return Ok(APIResponse<WalletBalanceResponse>.Success(result, ApiMessages.Success));
        }
        catch (BookingException ex)
        {
            return StatusCode(ex.HttpStatus, new { errorCode = ex.ErrorCode, message = ex.Message });
        }
    }

    [HttpGet("transactions")]
    [Authorize(Roles = UserRole.ParentOrStudentOrTutor)]
    public async Task<IActionResult> GetTransactions([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var userId = UserId;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));

        try
        {
            var result = await walletService.GetTransactionHistoryAsync(userId, page, pageSize);
            return Ok(APIResponse<TransactionHistoryPagedResponse>.Success(result, ApiMessages.Success));
        }
        catch (BookingException ex)
        {
            return StatusCode(ex.HttpStatus, new { errorCode = ex.ErrorCode, message = ex.Message });
        }
    }

    [HttpGet("transactions/{id}")]
    [Authorize(Roles = UserRole.ParentOrStudentOrTutor)]
    public async Task<IActionResult> GetTransactionDetail(int id, CancellationToken ct)
    {
        var userId = UserId;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));

        try
        {
            var result = await walletService.GetTransactionDetailAsync(userId, id, ct);
            return Ok(APIResponse<TransactionHistoryResponse>.Success(result, ApiMessages.Success));
        }
        catch (NotFoundException ex)
        {
            return NotFound(APIResponse.Fail(ex.Message, 404));
        }
        catch (BookingException ex)
        {
            return StatusCode(ex.HttpStatus, new { errorCode = ex.ErrorCode, message = ex.Message });
        }
    }

    /// <summary>
    /// Parent/Student: create a withdrawal with the bank destination supplied directly in the request
    /// (no saved BankAccount fallback). Tutors keep using POST /api/tutor/withdrawals, which only
    /// ever pays out to their saved account — this endpoint must not become a way to bypass that.
    /// </summary>
    [HttpPost("withdrawals")]
    [Authorize(Roles = UserRole.ParentOrStudent)]
    public async Task<IActionResult> CreateWithdrawal([FromBody] CreateWithdrawalRequest request, CancellationToken ct)
    {
        var userId = UserId;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));

        try
        {
            var result = await walletService.CreateWithdrawalAsync(userId, request, ct);
            return Ok(APIResponse<WithdrawalDetailResponse>.Success(result, "Tạo yêu cầu rút tiền thành công."));
        }
        catch (BadRequestException ex)
        {
            return BadRequest(APIResponse.Fail(ex.Message));
        }
        catch (NotFoundException ex)
        {
            return NotFound(APIResponse.Fail(ex.Message, 404));
        }
    }

    [HttpGet("withdrawals")]
    [Authorize(Roles = UserRole.ParentOrStudentOrTutor)]
    public async Task<IActionResult> GetWithdrawals(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var userId = UserId;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));

        try
        {
            var result = await walletService.GetWithdrawalsAsync(userId, page, pageSize, ct);
            return Ok(APIResponse<WithdrawalListResponse>.Success(result, ApiMessages.Success));
        }
        catch (BookingException ex)
        {
            return StatusCode(ex.HttpStatus, new { errorCode = ex.ErrorCode, message = ex.Message });
        }
    }

    [HttpGet("withdrawals/{id}")]
    [Authorize(Roles = UserRole.ParentOrStudentOrTutor)]
    public async Task<IActionResult> GetWithdrawalDetail(int id, CancellationToken ct)
    {
        var userId = UserId;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));

        try
        {
            var result = await walletService.GetWithdrawalDetailAsync(userId, id, ct);
            return Ok(APIResponse<WithdrawalDetailResponse>.Success(result, ApiMessages.Success));
        }
        catch (NotFoundException ex)
        {
            return NotFound(APIResponse.Fail(ex.Message, 404));
        }
    }
}
