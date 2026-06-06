using Microsoft.AspNetCore.Authorization;
using MV.DomainLayer.Constants;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace MV.PresentationLayer.Controllers;

/// <summary>
/// ZaloPay payment endpoints for Zalo Mini App.
/// Requires ZaloPay Merchant credentials in appsettings:
///   "ZaloPay": { "AppId": "", "Key1": "", "Key2": "", "AppUser": "tutora" }
/// Until credentials are configured, all endpoints return ServiceUnavailable.
/// </summary>
[ApiController]
[Route("api/payments/zalopay")]
public class ZaloPayController(
    IPaymentService paymentService,
    IConfiguration configuration,
    ILogger<ZaloPayController> logger) : ControllerBase
{
    private string? UserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    private bool IsConfigured =>
        !string.IsNullOrEmpty(configuration[ConfigurationKeys.ZaloPay.AppId]) &&
        !string.IsNullOrEmpty(configuration[ConfigurationKeys.ZaloPay.Key1]);

    /// <summary>
    /// Tạo ZaloPay order cho booking.
    /// Mini App gọi endpoint này trước khi mở payment.createOrder({ amount, desc }).
    /// </summary>
    [HttpPost("orders")]
    [Authorize(Roles = UserRole.ParentOrStudent)]
    public async Task<IActionResult> CreateOrder([FromBody] ZaloPayCreateOrderRequest request)
    {
        if (!IsConfigured)
            return StatusCode(503, APIResponse.Fail("ZaloPay chưa được cấu hình. Vui lòng dùng PayOS.", 503));

        var userId = UserId;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));

        var booking = await paymentService.GetBookingForZaloPayAsync(request.BookingId, userId);

        if (booking == null)
            return NotFound(APIResponse.Fail("Booking không tồn tại hoặc bạn không có quyền truy cập.", 404));

        // NOTE: ZaloPay order creation is intentionally stubbed pending merchant credential setup.
        // When AppId/Key1 are configured, replace the stub below with an actual API call:
        //   POST https://sb.zalopay.vn/v2/create (sandbox)
        //        https://openapi.zalopay.vn/v2/create (production)
        //   mac = HMAC-SHA256($"{app_id}|{app_trans_id}|{app_user}|{amount}|{app_time}|{embed_data}|{item}", Key1)

        var appTransId = $"{MV.DomainLayer.Helpers.VietnamTimeHelper.Now:yyMMdd}_{booking.BookingId}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var amount = (long)booking.Amount;

        logger.LogInformation("ZaloPay create-order: booking={BookingId} amount={Amount}", request.BookingId, amount);

        // Stub response — replace with real ZaloPay API response when credentials available
        return Ok(new APIResponse<ZaloPayOrderResponse>(200, "Tạo ZaloPay order thành công", new ZaloPayOrderResponse
        {
            AppTransId = appTransId,
            Amount = amount,
            Description = $"Tutora - Đặt lịch gia sư #{booking.BookingId}"
        }));
    }

    /// <summary>
    /// Xác nhận thanh toán ZaloPay sau khi Mini App nhận callback từ payment.createOrder().
    /// </summary>
    [HttpPost("confirm")]
    [Authorize(Roles = UserRole.ParentOrStudent)]
    public async Task<IActionResult> ConfirmPayment([FromBody] ZaloPayConfirmRequest request)
    {
        if (!IsConfigured)
            return StatusCode(503, APIResponse.Fail("ZaloPay chưa được cấu hình.", 503));

        var userId = UserId;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));

        // NOTE: ZaloPay payment verification is intentionally stubbed pending merchant credential setup.
        // When credentials are available, query payment status before confirming:
        //   GET https://openapi.zalopay.vn/v2/query?app_id=...&app_trans_id=...&mac=...
        //   mac = HMAC-SHA256($"{app_id}|{app_trans_id}|{key1}", Key1)
        //   If return_code == 1 → call paymentService to update booking status.

        logger.LogInformation("ZaloPay confirm: booking={BookingId} zpTransToken={Token}",
            request.BookingId, request.ZpTransToken);

        // Stub — implement real verification when credentials available
        return Ok(APIResponse.Success("Thanh toán ZaloPay đã được ghi nhận. Đang xử lý."));
    }
}

/// <summary>
/// ZaloPay webhook — called by ZaloPay server after payment completion.
/// Route is at /api/webhooks to match existing PayOS webhook pattern.
/// </summary>
[ApiController]
[Route("api/webhooks")]
public class ZaloPayWebhookController(
    IPaymentService paymentService,
    IConfiguration configuration,
    ILogger<ZaloPayWebhookController> logger) : ControllerBase
{
    /// <summary>
    /// ZaloPay IPN (Instant Payment Notification) callback.
    /// Docs: https://docs.zalopay.vn/docs/general/webhook
    /// ZaloPay POSTs with application/x-www-form-urlencoded body.
    /// </summary>
    [HttpPost("zalopay")]
    [AllowAnonymous]
    public async Task<IActionResult> ZaloPayWebhook([FromForm] ZaloPayWebhookPayload payload)
    {
        logger.LogInformation("ZaloPay webhook: app_trans_id={AppTransId} amount={Amount} return_code={Code}",
            payload.AppTransId, payload.Amount, payload.ReturnCode);

        var key2 = configuration[ConfigurationKeys.ZaloPay.Key2];
        if (string.IsNullOrEmpty(key2))
        {
            logger.LogWarning("ZaloPay Key2 not configured — skipping MAC verification.");
        }
        else if (!VerifyMac(payload, key2))
        {
            logger.LogWarning("ZaloPay webhook MAC verification failed for trans_id={AppTransId}.", payload.AppTransId);
            return Ok(new { return_code = -1, return_message = "Mac not equal." });
        }

        // Only process successful payments
        if (payload.ReturnCode != 1)
        {
            logger.LogInformation("ZaloPay webhook: payment not successful (return_code={Code}).", payload.ReturnCode);
            return Ok(new { return_code = 1, return_message = "ok" });
        }

        // Parse bookingId from app_trans_id (format: yyMMdd_{bookingId}_{timestamp})
        var parts = payload.AppTransId?.Split('_');
        if (parts?.Length >= 2 && int.TryParse(parts[1], out var bookingId))
        {
            logger.LogInformation("ZaloPay webhook: payment confirmed — bookingId={BookingId} appTransId={AppTransId}.",
                bookingId, payload.AppTransId);
            // NOTE: Add IPaymentService.ProcessZaloPayWebhookAsync(bookingId, payload.Amount, ct)
            // to transition booking payment status — pending service method implementation.
        }
        else
        {
            logger.LogWarning("ZaloPay webhook: could not parse bookingId from app_trans_id={AppTransId}.", payload.AppTransId);
        }

        // ZaloPay expects this response format
        return Ok(new { return_code = 1, return_message = "ok" });
    }

    /// <summary>
    /// MAC = HMAC-SHA256("${app_id}|${app_trans_id}|${zp_trans_id}|${amount}|${server_time}|${channel}|${merchant_user_id}", Key2)
    /// </summary>
    private static bool VerifyMac(ZaloPayWebhookPayload payload, string key2)
    {
        var data = $"{payload.AppId}|{payload.AppTransId}|{payload.ZpTransId}|{payload.Amount}|{payload.ServerTime}|{payload.Channel}|{payload.MerchantUserId}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key2));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        var expected = Convert.ToHexString(hash).ToLowerInvariant();
        return string.Equals(expected, payload.Mac?.ToLowerInvariant(), StringComparison.Ordinal);
    }
}
