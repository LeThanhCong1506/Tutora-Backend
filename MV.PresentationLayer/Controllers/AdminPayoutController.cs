using MV.DomainLayer.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel.Admin;
using MV.DomainLayer.DTO.ResponseModel.Admin;
using MV.PresentationLayer.Authorization;
using System.Security.Claims;

namespace MV.PresentationLayer.Controllers;

[ApiController]
[Route("api/admin/payouts")]
[Authorize]
public class AdminPayoutController(
    IAdminPayoutService adminPayoutService,
    ISystemAlertService systemAlertService) : ControllerBase
{
    private string? ActorUserId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    private string? ActorRole =>
        User.IsInRole(UserRole.Admin) ? UserRole.Admin :
        User.IsInRole(UserRole.Staff) ? UserRole.Staff :
        null;

    private IActionResult ValidatePagination(int page, int pageSize) =>
        page < 1 || pageSize is < 1 or > 100
            ? BadRequest(APIResponse<object>.Fail("Tham số phân trang không hợp lệ.", 400))
            : null!;

    private IActionResult HandleException(Exception ex, string operation) => ex switch
    {
        KeyNotFoundException => NotFound(APIResponse<object>.Fail("Không tìm thấy dữ liệu yêu cầu.", 404)),
        InvalidOperationException => BadRequest(APIResponse<object>.Fail(ex.Message, 400)),
        _ => StatusCode(500, APIResponse<object>.Fail($"Lỗi hệ thống: {ex.Message}", 500))
    };
    [RequirePermission(Permissions.PayoutView)]
    [HttpGet("overview")]
    public async Task<IActionResult> GetOverview(CancellationToken ct)
    {
        try
        {
            var overview = await adminPayoutService.GetOverviewAsync(ct);
            return Ok(APIResponse<PayoutOverviewResponse>.Success(overview, "Lấy tổng quan thành công."));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "retrieving overview");
        }
    }

    [RequirePermission(Permissions.PayoutView)]
    [HttpGet("pending")]
    public async Task<IActionResult> GetPendingReview(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        try
        {
            var validation = ValidatePagination(page, pageSize);
            if (validation != null) return validation;

            var result = await adminPayoutService.GetPendingReviewAsync(page, pageSize, ct);
            return Ok(APIResponse<PendingReviewResponse>.Success(result, "Lấy danh sách chờ duyệt thành công."));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "retrieving pending review");
        }
    }

    [RequirePermission(Permissions.PayoutView)]
    [HttpGet]
    public async Task<IActionResult> GetAllRequests(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        [FromQuery] string? search = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        CancellationToken ct = default)
    {
        try
        {
            var validation = ValidatePagination(page, pageSize);
            if (validation != null) return validation;

            var result = await adminPayoutService.GetAllRequestsAsync(page, pageSize, status, search, from, to, ct);
            return Ok(APIResponse<MV.DomainLayer.DTO.ResponseModel.WithdrawalListResponse>.Success(result, "Lấy danh sách yêu cầu thành công."));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "retrieving requests");
        }
    }

    [RequirePermission(Permissions.PayoutView)]
    [HttpGet("{id}")]
    public async Task<IActionResult> GetRequestDetail(int id, CancellationToken ct)
    {
        try
        {
            var detail = await adminPayoutService.GetRequestDetailAsync(id, ct);
            return Ok(APIResponse<AdminWithdrawalDetailResponse>.Success(detail, "Lấy chi tiết yêu cầu thành công."));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "retrieving request detail");
        }
    }

    [RequirePermission(Permissions.PayoutApprove)]
    [HttpPost("{id}/claim")]
    public async Task<IActionResult> ClaimRequest(int id, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrEmpty(ActorUserId))
                return Unauthorized(APIResponse<object>.Fail(ApiMessages.ActorUserIdNotFound, 401));

            var result = await adminPayoutService.ClaimRequestAsync(id, ActorUserId, ct);
            return Ok(APIResponse<ApproveResult>.Success(result, result.Message));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "claiming request");
        }
    }

    [RequirePermission(Permissions.PayoutApprove)]
    [HttpPost("{id}/release")]
    public async Task<IActionResult> ReleaseRequest(int id, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrEmpty(ActorUserId))
                return Unauthorized(APIResponse<object>.Fail(ApiMessages.ActorUserIdNotFound, 401));

            var result = await adminPayoutService.ReleaseRequestAsync(id, ActorUserId, ct);
            return Ok(APIResponse<ApproveResult>.Success(result, result.Message));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "releasing request");
        }
    }

    [RequirePermission(Permissions.PayoutApprove)]
    [HttpPost("{id}/approve")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> ApproveRequest(
        int id,
        [FromForm] ApproveWithdrawalRequest request,
        CancellationToken ct)
    {
        try
        {
            var actorUserId = ActorUserId;
            var actorRole = ActorRole;
            if (string.IsNullOrEmpty(actorUserId))
                return Unauthorized(APIResponse<object>.Fail(ApiMessages.ActorUserIdNotFound, 401));
            if (actorRole == null)
                return Forbid();

            var result = await adminPayoutService.ApproveRequestAsync(id, actorUserId, actorRole, request, ct);
            return result.Success
                ? Ok(APIResponse<ApproveResult>.Success(result, "Duyệt yêu cầu rút tiền thành công."))
                : BadRequest(APIResponse<object>.Fail(result.Message, 400));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "approving request");
        }
    }

    [RequirePermission(Permissions.PayoutReject)]
    [HttpPost("{id}/reject")]
    public async Task<IActionResult> RejectRequest(
        int id,
        [FromBody] RejectWithdrawalRequest request,
        CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrEmpty(ActorUserId))
                return Unauthorized(APIResponse<object>.Fail(ApiMessages.ActorUserIdNotFound, 401));

            var result = await adminPayoutService.RejectRequestAsync(id, ActorUserId, request.Reason, ct);
            return result.Success
                ? Ok(APIResponse<RejectResult>.Success(result, "Từ chối yêu cầu rút tiền thành công."))
                : BadRequest(APIResponse<object>.Fail(result.Message, 400));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "rejecting request");
        }
    }

    /// <summary>
    /// Chủ động chuyển tiền vào ví một user (gia sư/phụ huynh/học sinh), không gắn với yêu
    /// cầu rút tiền nào. Cộng thẳng vào số dư ngay khi gọi — không có bước duyệt thứ hai.
    /// </summary>
    [RequirePermission(Permissions.PayoutTransfer)]
    [HttpPost("transfers")]
    public async Task<IActionResult> TransferToUser(
        [FromBody] AdminWalletTransferRequest request,
        CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrEmpty(ActorUserId))
                return Unauthorized(APIResponse<object>.Fail(ApiMessages.ActorUserIdNotFound, 401));

            var result = await adminPayoutService.TransferToUserAsync(ActorUserId, request, ct);
            return Ok(APIResponse<AdminWalletTransferResponse>.Success(result, "Chuyển tiền thành công."));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "transferring to user");
        }
    }

    /// <summary>
    /// Lịch sử các lần chuyển tiền chủ động.
    /// </summary>
    [RequirePermission(Permissions.PayoutView)]
    [HttpGet("transfers")]
    public async Task<IActionResult> GetTransferHistory(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var validation = ValidatePagination(page, pageSize);
        if (validation != null) return validation;

        try
        {
            var result = await adminPayoutService.GetTransferHistoryAsync(page, pageSize, ct);
            return Ok(APIResponse<AdminWalletTransferListResponse>.Success(result, "Lấy lịch sử chuyển tiền thành công."));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "retrieving transfer history");
        }
    }

    /// <summary>
    /// Số dư hiện tại của quỹ hệ thống — nguồn duy nhất "Chuyển tiền chủ động" được trừ vào.
    /// </summary>
    [RequirePermission(Permissions.PayoutView)]
    [HttpGet("fund")]
    public async Task<IActionResult> GetFundBalance(CancellationToken ct)
    {
        try
        {
            var result = await adminPayoutService.GetFundBalanceAsync(ct);
            return Ok(APIResponse<SystemFundResponse>.Success(result, "Lấy số dư quỹ hệ thống thành công."));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "retrieving fund balance");
        }
    }

    /// <summary>
    /// Nạp tiền thật (kèm ảnh chứng minh) vào quỹ hệ thống.
    /// </summary>
    [RequirePermission(Permissions.PayoutFundTopup)]
    [HttpPost("fund/topup")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> TopUpFund(
        [FromForm] SystemFundTopupRequest request,
        CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrEmpty(ActorUserId))
                return Unauthorized(APIResponse<object>.Fail(ApiMessages.ActorUserIdNotFound, 401));

            var result = await adminPayoutService.TopUpFundAsync(ActorUserId, request, ct);
            return Ok(APIResponse<SystemFundTopupResponse>.Success(result, "Nạp quỹ hệ thống thành công."));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "topping up system fund");
        }
    }

    /// <summary>
    /// Lịch sử các lần nạp quỹ hệ thống.
    /// </summary>
    [RequirePermission(Permissions.PayoutView)]
    [HttpGet("fund/topups")]
    public async Task<IActionResult> GetFundTopupHistory(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var validation = ValidatePagination(page, pageSize);
        if (validation != null) return validation;

        try
        {
            var result = await adminPayoutService.GetFundTopupHistoryAsync(page, pageSize, ct);
            return Ok(APIResponse<SystemFundTopupListResponse>.Success(result, "Lấy lịch sử nạp quỹ thành công."));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "retrieving fund top-up history");
        }
    }

    [HttpGet("system-alerts")]
    [RequirePermission(Permissions.SystemAlertView)]
    public async Task<IActionResult> GetSystemAlerts(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool? resolved = null,
        CancellationToken ct = default)
    {
        try
        {
            var validation = ValidatePagination(page, pageSize);
            if (validation != null) return validation;

            var result = await systemAlertService.GetAlertsAsync(page, pageSize, resolved, ct);
            return Ok(APIResponse<SystemAlertResponse>.Success(result, "Lấy danh sách cảnh báo hệ thống thành công."));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "retrieving system alerts");
        }
    }

    [HttpPost("system-alerts/{id}/resolve")]
    [RequirePermission(Permissions.SystemAlertResolve)]
    public async Task<IActionResult> ResolveAlert(int id, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrEmpty(ActorUserId))
                return Unauthorized(APIResponse<object>.Fail(ApiMessages.AdminUserIdNotFound, 401));

            var success = await systemAlertService.ResolveAlertAsync(id, ActorUserId, ct);
            return success
                ? Ok(APIResponse<object>.Success(null, "Xử lý cảnh báo thành công."))
                : NotFound(APIResponse<object>.Fail("Không tìm thấy cảnh báo.", 404));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "resolving alert");
        }
    }
}
