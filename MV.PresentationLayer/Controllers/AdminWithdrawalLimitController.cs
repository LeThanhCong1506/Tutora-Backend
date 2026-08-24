using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel.Admin;
using MV.DomainLayer.Exceptions;
using MV.PresentationLayer.Authorization;

namespace MV.PresentationLayer.Controllers;

/// <summary>Admin quản lý ngưỡng rút tiền tối thiểu của nền tảng.</summary>
[ApiController]
[Route("api/admin/withdrawal-limit")]
[Authorize]
public class AdminWithdrawalLimitController(IWithdrawalLimitService withdrawalLimitService) : ControllerBase
{
    private string? UserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    [HttpGet("config")]
    [RequirePermission(Permissions.FinancialView)]
    public async Task<ActionResult<APIResponse<AdminWithdrawalLimitResponse>>> GetConfig(CancellationToken ct)
    {
        var config = await withdrawalLimitService.AdminGetAsync(ct);
        return Ok(APIResponse<AdminWithdrawalLimitResponse>.Success(config));
    }

    [HttpPut("config")]
    [RequirePermission(Permissions.FinancialManage)]
    public async Task<ActionResult<APIResponse<AdminWithdrawalLimitResponse>>> SetConfig(
        [FromBody] AdminSetWithdrawalLimitRequest request, CancellationToken ct)
    {
        try
        {
            var config = await withdrawalLimitService.AdminSetAsync(request, UserId, ct);
            return Ok(APIResponse<AdminWithdrawalLimitResponse>.Success(config, "Đã cập nhật ngưỡng rút tiền tối thiểu."));
        }
        catch (BookingException ex)
        {
            return StatusCode(ex.HttpStatus, APIResponse<AdminWithdrawalLimitResponse>.Fail(ex.Message, ex.HttpStatus));
        }
    }
}
