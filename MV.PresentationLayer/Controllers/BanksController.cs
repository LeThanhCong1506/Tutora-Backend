using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.Interfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.BankVerification;

namespace MV.PresentationLayer.Controllers;

[ApiController]
[Route("api/banks")]
public class BanksController(IBankVerificationService bankVerificationService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<APIResponse<List<BankInfoResponse>>>> GetBankList(CancellationToken cancellationToken)
    {
        try
        {
            var banks = await bankVerificationService.GetBankListAsync(cancellationToken);
            return Ok(APIResponse<List<BankInfoResponse>>.Success(banks, "Lấy danh sách ngân hàng thành công."));
        }
        catch
        {
            return StatusCode(500, APIResponse<List<BankInfoResponse>>.Fail("Đã xảy ra lỗi khi lấy danh sách ngân hàng.", 500, new { ErrorCode = ApiErrorCodes.InternalError }));
        }
    }
}
