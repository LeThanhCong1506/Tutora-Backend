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

/// <summary>Admin quản lý phí sàn (commission) cho phụ huynh và gia sư.</summary>
[ApiController]
[Route("api/admin/commission")]
[Authorize]
public class AdminCommissionController(ICommissionConfigService commissionConfigService) : ControllerBase
{
    private string? UserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    [HttpGet("config")]
    [RequirePermission(Permissions.FinancialView)]
    public async Task<ActionResult<APIResponse<AdminCommissionConfigResponse>>> GetConfig(CancellationToken ct)
    {
        var config = await commissionConfigService.AdminGetAsync(ct);
        return Ok(APIResponse<AdminCommissionConfigResponse>.Success(config));
    }

    [HttpPut("config")]
    [RequirePermission(Permissions.FinancialManage)]
    public async Task<ActionResult<APIResponse<AdminCommissionConfigResponse>>> SetConfig(
        [FromBody] AdminSetCommissionConfigRequest request, CancellationToken ct)
    {
        try
        {
            var config = await commissionConfigService.AdminSetAsync(request, UserId, ct);
            return Ok(APIResponse<AdminCommissionConfigResponse>.Success(config, "Đã cập nhật phí sàn."));
        }
        catch (BookingException ex)
        {
            return StatusCode(ex.HttpStatus, APIResponse<AdminCommissionConfigResponse>.Fail(ex.Message, ex.HttpStatus));
        }
    }
}
