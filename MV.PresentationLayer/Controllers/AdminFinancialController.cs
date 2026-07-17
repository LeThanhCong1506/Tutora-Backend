using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.ResponseModel.Admin;
using MV.DomainLayer.Exceptions;
using MV.PresentationLayer.Authorization;

namespace MV.PresentationLayer.Controllers;

[ApiController]
[Route("api/admin/financials")]
[Authorize]
[RequirePermission(Permissions.FinancialView)]
public class AdminFinancialController(IAdminFinancialService adminFinancialService) : ControllerBase
{
    private IActionResult HandleException(Exception ex) => ex switch
    {
        ArgumentException => BadRequest(APIResponse<object>.Fail(ex.Message, 400)),
        TransactionNotFoundException => NotFound(APIResponse<object>.Fail(ex.Message, 404)),
        _ => StatusCode(500, APIResponse<object>.Fail($"Lỗi hệ thống: {ex.Message}", 500))
    };

    private IActionResult ValidatePagination(int page, int pageSize) =>
        page < 1 || pageSize is < 1 or > 100
            ? BadRequest(APIResponse<object>.Fail("Tham số phân trang không hợp lệ.", 400))
            : null!;

    /// <summary>
    /// GET /api/admin/financials/metrics
    /// Returns all financial KPI metrics for the admin dashboard.
    /// </summary>
    [HttpGet("metrics")]
    public async Task<IActionResult> GetMetrics(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] string period = "month",
        CancellationToken ct = default)
    {
        try
        {
            if (!string.IsNullOrEmpty(period) && period is not ("month" or "week" or "year"))
                return BadRequest(APIResponse<object>.Fail("Tham số period không hợp lệ. Giá trị hợp lệ: month, week, year.", 400));

            if (from.HasValue && to.HasValue && from > to)
                return BadRequest(APIResponse<object>.Fail("Ngày bắt đầu không được lớn hơn ngày kết thúc.", 400));

            var result = await adminFinancialService.GetMetricsAsync(from, to, period, ct);
            return Ok(APIResponse<AdminFinancialMetricsResponse>.Success(result, "Lấy số liệu tài chính thành công."));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    /// <summary>
    /// GET /api/admin/financials/transactions
    /// Returns a paginated list of all wallet transactions across the platform.
    /// Query params: page, pageSize, type, userId, from, to, search
    /// </summary>
    [HttpGet("transactions")]
    public async Task<IActionResult> GetTransactions(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? type = null,
        [FromQuery] string? userId = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] string? search = null,
        CancellationToken ct = default)
    {
        try
        {
            var validation = ValidatePagination(page, pageSize);
            if (validation != null) return validation;

            if (from.HasValue && to.HasValue && from > to)
                return BadRequest(APIResponse<object>.Fail("Ngày bắt đầu không được lớn hơn ngày kết thúc.", 400));

            var result = await adminFinancialService.GetTransactionsAsync(
                page, pageSize, type, userId, from, to, search, ct);

            return Ok(APIResponse<AdminTransactionListResponse>.Success(result, "Lấy danh sách giao dịch thành công."));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    /// <summary>
    /// GET /api/admin/financials/payment-transactions
    /// Returns recorded payment movements, not wallet balance ledger entries.
    /// </summary>
    [HttpGet("payment-transactions")]
    public async Task<IActionResult> GetPaymentTransactions(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? paymentMethod = null,
        [FromQuery] string? direction = null,
        [FromQuery] string? purpose = null,
        [FromQuery] string? status = null,
        [FromQuery] string? reconciliationStatus = null,
        [FromQuery] string? userId = null,
        [FromQuery] int? bookingId = null,
        [FromQuery] int? withdrawalId = null,
        [FromQuery] int? paymentRequestId = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] string? search = null,
        CancellationToken ct = default)
    {
        try
        {
            var validation = ValidatePagination(page, pageSize);
            if (validation != null) return validation;

            if (from.HasValue && to.HasValue && from.Value > to.Value)
                return BadRequest(APIResponse<object>.Fail("Ngày bắt đầu không được lớn hơn ngày kết thúc.", 400));

            var result = await adminFinancialService.GetPaymentTransactionsAsync(
                page,
                pageSize,
                paymentMethod,
                direction,
                purpose,
                status,
                reconciliationStatus,
                userId,
                bookingId,
                withdrawalId,
                paymentRequestId,
                from,
                to,
                search,
                ct);

            return Ok(APIResponse<AdminPaymentTransactionListResponse>.Success(
                result,
                "Lấy danh sách giao dịch thanh toán thành công."));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    /// <summary>GET /api/admin/financials/payment-transactions/{id}</summary>
    [HttpGet("payment-transactions/{id:int}")]
    public async Task<IActionResult> GetPaymentTransactionDetail(int id, CancellationToken ct)
    {
        try
        {
            var result = await adminFinancialService.GetPaymentTransactionDetailAsync(id, ct);
            return Ok(APIResponse<AdminPaymentTransactionDetailResponse>.Success(
                result,
                "Lấy chi tiết giao dịch tiền thật thành công."));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }
}
