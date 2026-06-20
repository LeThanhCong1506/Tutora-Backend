using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.Helpers;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Exceptions;
using System.Security.Claims;
using System.Text.Json;

namespace MV.PresentationLayer.Controllers;

[ApiController]
[Route("api")]
public class PaymentController(
    IPaymentService paymentService,
    IWalletService walletService,
    ILogger<PaymentController> logger) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private string? UserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    [HttpGet("bookings/{id}/payment")]
    [Authorize(Roles = UserRole.ParentOrStudent)]
    public async Task<IActionResult> GetPaymentInfo([FromRoute] int id)
    {
        var userId = UserId;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));

        try
        {
            var result = await paymentService.GetPaymentInfoAsync(id, userId);
            return Ok(APIResponse<PaymentInfoResponse>.Success(result, "Tạo link thanh toán thành công."));
        }
        catch (BookingException ex)
        {
            return StatusCode(ex.HttpStatus, new { errorCode = ex.ErrorCode, message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error in GetPaymentInfo for booking {BookingId}", id);
            return StatusCode(500, new { errorCode = ApiErrorCodes.InternalError, message = "Lỗi hệ thống khi tạo link thanh toán." });
        }
    }

    [HttpGet("bookings/{id}/payment/status")]
    [Authorize(Roles = UserRole.ParentOrStudent)]
    public async Task<IActionResult> GetPaymentStatus([FromRoute] int id)
    {
        var userId = UserId;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));

        try
        {
            var result = await paymentService.GetPaymentStatusAsync(id, userId);
            return Ok(APIResponse<PaymentStatusResponse>.Success(result, ApiMessages.Success));
        }
        catch (BookingException ex)
        {
            return StatusCode(ex.HttpStatus, new { errorCode = ex.ErrorCode, message = ex.Message });
        }
    }

    [HttpPost("bookings/{id}/pay/wallet")]
    [Authorize(Roles = UserRole.ParentOrStudent)]
    public async Task<IActionResult> PayWithWallet([FromRoute] int id)
    {
        var userId = UserId;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));

        try
        {
            await paymentService.PayWithWalletAsync(id, userId, HttpContext.RequestAborted);
            return Ok(APIResponse.Success("Thanh toán thành công."));
        }
        catch (BookingException ex)
        {
            return StatusCode(ex.HttpStatus, new { errorCode = ex.ErrorCode, message = ex.Message });
        }
    }

    [HttpPost("webhooks/payos")]
    [AllowAnonymous]
    public async Task<IActionResult> PayOSWebhook()
    {
        var rawPayload = await new StreamReader(Request.Body).ReadToEndAsync();
        logger.LogInformation("PayOS webhook received: {Payload}", rawPayload);

        if (!await paymentService.VerifyWebhookSignatureAsync(rawPayload, Request.Headers["x-api-key"].ToString()))
        {
            logger.LogWarning("Invalid webhook signature");
            return Unauthorized(new { errorCode = BookingErrorCodes.InvalidSignature, message = "Invalid signature." });
        }

        PaymentWebhookRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<PaymentWebhookRequest>(rawPayload, JsonOptions);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to deserialize webhook");
            return BadRequest(new { errorCode = BookingErrorCodes.InvalidWebhookPayload, message = "Invalid payload." });
        }

        if (request?.Data == null)
            return BadRequest(new { errorCode = BookingErrorCodes.InvalidWebhookPayload, message = "Invalid payload." });
        try
        {
            var orderCode = request.Data.OrderCode;

            if (OrderCodeHelper.IsBookingOrderCode(orderCode) || OrderCodeHelper.IsRemainingOrderCode(orderCode))
            {
                logger.LogInformation("Processing booking payment webhook for orderCode: {OrderCode}", orderCode);
                await paymentService.ProcessWebhookAsync(request, HttpContext.RequestAborted);
            }
            else if (OrderCodeHelper.IsTopupOrderCode(orderCode))
            {
                logger.LogInformation("Processing topup webhook for orderCode: {OrderCode}", orderCode);
                await walletService.ProcessTopupWebhookAsync(request, HttpContext.RequestAborted);
            }
            else
            {
                logger.LogWarning("Test or invalid orderCode: {OrderCode} - Ignoring", orderCode);
                return Ok(new { code = PayOSWebhookCode.SuccessCode, desc = PayOSWebhookCode.SuccessDesc, data = new { } });
            }

            return Ok(new { code = PayOSWebhookCode.SuccessCode, desc = PayOSWebhookCode.SuccessDesc, data = new { } });
        }
        catch (BookingException ex)
        {
            logger.LogError(ex, "Webhook processing error");
            return StatusCode(ex.HttpStatus, new { errorCode = ex.ErrorCode, message = ex.Message });
        }
    }

    [HttpPost("admin/bookings/{id}/payment/confirm")]
    [Authorize(Roles = UserRole.Admin)]
    public async Task<IActionResult> ConfirmPayment([FromRoute] int id, [FromBody] AdminConfirmPaymentRequest request)
    {
        try
        {
            await paymentService.ConfirmPaymentByAdminAsync(id, request, HttpContext.RequestAborted);
            return Ok(APIResponse.Success("Xác nhận thanh toán thành công."));
        }
        catch (BookingException ex)
        {
            return StatusCode(ex.HttpStatus, new { errorCode = ex.ErrorCode, message = ex.Message });
        }
    }

    [HttpGet("tutor/wallet/summary")]
    [Authorize(Roles = UserRole.Tutor)]
    public async Task<IActionResult> GetWalletSummary()
    {
        var tutorId = UserId;
        if (string.IsNullOrEmpty(tutorId))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));

        var result = await paymentService.GetTutorWalletSummaryAsync(tutorId);
        return Ok(APIResponse<WalletSummaryResponse>.Success(result, ApiMessages.Success));
    }
}

/// <summary>
/// Handles PayOS payment callback redirects (loaded inside iframe after payment).
/// Serves a minimal HTML page that sends postMessage to the parent window.
/// </summary>
[ApiController]
public class PaymentCallbackController : ControllerBase
{
    [HttpGet("/payment/success")]
    [AllowAnonymous]
    public IActionResult PaymentSuccess(
        [FromQuery] string? code,
        [FromQuery] string? id,
        [FromQuery] string? status,
        [FromQuery] string? orderCode,
        [FromQuery] bool cancel = false)
    {
        var isPaid = status?.Equals(PayOSLinkStatus.Paid, StringComparison.OrdinalIgnoreCase) == true && code == PayOSWebhookCode.SuccessCode;
        return Content(BuildCallbackHtml(isPaid, cancel, status, orderCode), "text/html");
    }

    [HttpGet("/payment/cancel")]
    [AllowAnonymous]
    public IActionResult PaymentCancel(
        [FromQuery] string? code,
        [FromQuery] string? id,
        [FromQuery] string? status,
        [FromQuery] string? orderCode)
    {
        return Content(BuildCallbackHtml(false, true, status, orderCode), "text/html");
    }

    private static string BuildCallbackHtml(bool isPaid, bool cancel, string? status, string? orderCode)
    {
        var emoji = isPaid ? "✅" : cancel ? "❌" : "⏳";
        var title = isPaid ? "Thanh toán thành công!" : cancel ? "Thanh toán đã bị hủy" : "Đang xử lý...";
        var isPaidJs = isPaid ? "true" : "false";
        var cancelJs = cancel ? "true" : "false";
        var safeStatus = status ?? "";
        var safeOrderCode = orderCode ?? "";

        return "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>" + title + "</title></head>"
                    + "<body style=\"display:flex;flex-direction:column;align-items:center;justify-content:center;height:100vh;font-family:sans-serif;background:#f9fafb;margin:0;text-align:center\">"
                    + "<div style=\"font-size:64px;margin-bottom:16px\">" + emoji + "</div>"
                    + "<h2 style=\"color:#1a2238;margin:0 0 8px\">" + title + "</h2>"
                    + "<p style=\"color:#6b7280;font-size:14px\">Trang sẽ tự động đóng trong giây lát...</p>"
                    + "<script>"
                    + "try { window.opener && window.opener.postMessage({ type: 'PAYOS_PAYMENT_RESULT', isPaid: " + isPaidJs + ", cancel: " + cancelJs + ", status: '" + safeStatus + "', orderCode: '" + safeOrderCode + "' }, '*'); }"
                    + " catch(e) { console.error('postMessage failed:', e); }"
                    + "setTimeout(function() { window.close(); }, 1500);"
                    + "</script></body></html>";
    }
}
