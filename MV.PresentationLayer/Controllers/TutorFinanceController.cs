using MV.DomainLayer.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.Exceptions;
using System.Security.Claims;

namespace MV.PresentationLayer.Controllers;

[ApiController]
[Route("api/tutor")]
[Authorize(Roles = UserRole.Tutor)]
public class TutorFinanceController(ITutorFinanceService financeService) : ControllerBase
{
    private string TutorId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private void SetNoCacheResponseHeaders()
    {
        Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
        Response.Headers.Pragma = "no-cache";
    }

    [HttpGet("finance/summary")]
    public async Task<IActionResult> GetSummary(CancellationToken ct)
    {
        SetNoCacheResponseHeaders();
        try
        {
            var result = await financeService.GetSummaryAsync(TutorId, ct);
            return Ok(APIResponse<object>.Success(result));
        }
        catch (NotFoundException ex)
        {
            return NotFound(APIResponse.Fail(ex.Message, 404));
        }
    }

    [HttpGet("finance/earnings")]
    public async Task<IActionResult> GetEarnings(
        [FromQuery] string period = EarningsPeriod.Month,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        CancellationToken ct = default)
    {
        SetNoCacheResponseHeaders();
        try
        {
            var result = await financeService.GetEarningsAsync(TutorId, period, from, to, ct);
            return Ok(APIResponse<object>.Success(result));
        }
        catch (BadRequestException ex)
        {
            return BadRequest(APIResponse.Fail(ex.Message));
        }
    }

    [HttpGet("finance/transactions")]
    public async Task<IActionResult> GetTransactions(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? type = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        CancellationToken ct = default)
    {
        SetNoCacheResponseHeaders();
        try
        {
            var result = await financeService.GetTransactionsAsync(TutorId, page, pageSize, type, from, to, ct);
            return Ok(APIResponse<object>.Success(result));
        }
        catch (Exception ex)
        {
            return StatusCode(500, APIResponse.Fail("Đã xảy ra lỗi.", 500));
        }
    }

    [HttpGet("finance/transactions/{id}")]
    public async Task<IActionResult> GetTransactionDetail(int id, CancellationToken ct)
    {
        SetNoCacheResponseHeaders();
        try
        {
            var result = await financeService.GetTransactionDetailAsync(TutorId, id, ct);
            return Ok(APIResponse<object>.Success(result));
        }
        catch (NotFoundException ex)
        {
            return NotFound(APIResponse.Fail(ex.Message, 404));
        }
    }

    // GET/PUT/DELETE bank — moved to BankAccountController (api/bank-account), shared with
    // Parent/Student, now OTP-gated.

    [HttpPost("withdrawals")]
    public async Task<IActionResult> CreateWithdrawal([FromBody] CreateWithdrawalRequest request, CancellationToken ct)
    {
        try
        {
            var result = await financeService.CreateWithdrawalAsync(TutorId, request, ct);
            return Ok(APIResponse<object>.Success(result, "Tạo yêu cầu rút tiền thành công."));
        }
        catch (BankInfoRequiredException ex)
        {
            return BadRequest(new { errorCode = "BANK_ACCOUNT_REQUIRED", message = ex.Message });
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
    public async Task<IActionResult> GetWithdrawals(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        SetNoCacheResponseHeaders();
        try
        {
            var result = await financeService.GetWithdrawalsAsync(TutorId, page, pageSize, ct);
            return Ok(APIResponse<object>.Success(result));
        }
        catch (Exception ex)
        {
            return StatusCode(500, APIResponse.Fail("Đã xảy ra lỗi.", 500));
        }
    }

    [HttpGet("withdrawals/{id}")]
    public async Task<IActionResult> GetWithdrawalDetail(int id, CancellationToken ct)
    {
        SetNoCacheResponseHeaders();
        try
        {
            var result = await financeService.GetWithdrawalDetailAsync(TutorId, id, ct);
            return Ok(APIResponse<object>.Success(result));
        }
        catch (NotFoundException ex)
        {
            return NotFound(APIResponse.Fail(ex.Message, 404));
        }
    }

    [HttpDelete("withdrawals/{id}")]
    public async Task<IActionResult> CancelWithdrawal(int id, CancellationToken ct)
    {
        try
        {
            await financeService.CancelWithdrawalAsync(TutorId, id, ct);
            return Ok(APIResponse.Success("Hủy yêu cầu rút tiền thành công."));
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
}
