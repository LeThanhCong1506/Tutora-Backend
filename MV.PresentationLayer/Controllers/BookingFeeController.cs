using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.ResponseModel;

namespace MV.PresentationLayer.Controllers;

/// <summary>
/// Tỷ lệ phí nền tảng đang áp dụng, cho phía đặt lịch đọc trước khi báo giá.
///
/// Tồn tại vì màn hình đặt lịch phải hiện đúng con số phụ huynh sẽ bị tính. Trước đây nó tự nhân
/// 5% ở phía trình duyệt, nên khi Admin đổi sang 10% thì modal vẫn báo 525.000đ trong khi hệ thống
/// thu 550.000đ — phụ huynh đồng ý một giá rồi bị trừ một giá khác.
///
/// Cho phép gọi ẩn danh: khách chưa đăng nhập vẫn xem được giá trên trang gia sư, và đây chỉ là
/// tham số giá công khai, không phải dữ liệu riêng của ai.
/// </summary>
[ApiController]
[Route("api/booking-fees")]
[AllowAnonymous]
public class BookingFeeController(ICommissionConfigService commissionConfigService) : ControllerBase
{
    /// <summary>GET /api/booking-fees/current — % phí phụ huynh trả thêm và % phí sàn trừ của gia sư.</summary>
    [HttpGet("current")]
    public async Task<ActionResult<APIResponse<BookingFeeRatesResponse>>> GetCurrent(CancellationToken ct)
    {
        var (parentPercent, tutorPercent) = await commissionConfigService.GetFeePercentsAsync(ct);

        return Ok(APIResponse<BookingFeeRatesResponse>.Success(new BookingFeeRatesResponse
        {
            ParentFeePercent = parentPercent,
            TutorFeePercent = tutorPercent
        }));
    }
}
