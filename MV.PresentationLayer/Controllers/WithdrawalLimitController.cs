using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.ResponseModel;

namespace MV.PresentationLayer.Controllers;

/// <summary>
/// Ngưỡng rút tiền tối thiểu ở góc nhìn người dùng cuối (chỉ đọc). Admin sửa giá trị này qua
/// <see cref="AdminWithdrawalLimitController"/>; ở đây chỉ trả về con số để FE hiển thị và chặn
/// sớm trên form, tránh việc FE hardcode một mức khác với mức backend đang thực sự áp dụng.
/// </summary>
[ApiController]
[Route("api/withdrawal-limit")]
[Authorize]
public class WithdrawalLimitController(IWithdrawalLimitService withdrawalLimitService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<APIResponse<WithdrawalLimitResponse>>> Get(CancellationToken ct)
    {
        var minWithdrawalAmount = await withdrawalLimitService.GetMinWithdrawalAmountAsync(ct);
        return Ok(APIResponse<WithdrawalLimitResponse>.Success(
            new WithdrawalLimitResponse { MinWithdrawalAmount = minWithdrawalAmount }));
    }
}
