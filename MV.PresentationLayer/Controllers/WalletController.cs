using MV.DomainLayer.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO;
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
    [Authorize(Roles = UserRole.ParentOrTutor)]
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
    [Authorize(Roles = UserRole.ParentOrTutor)]
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
}
