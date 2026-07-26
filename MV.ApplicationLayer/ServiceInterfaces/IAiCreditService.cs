using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces;

/// <summary>
/// AI credit (Homework Helper): cấp/tiêu credit theo tài khoản, quản lý gói, mua gói.
/// </summary>
public interface IAiCreditService
{
    /// <summary>Cấp credit cho tài khoản (dương). Ghi ledger + cộng cache balance trong 1 transaction.
    Task<int> GrantAsync(string userId, int amount, string source, string? referenceId, string? description, CancellationToken ct = default);

    /// <summary>Tiêu credit của tài khoản (truyền amount dương, hàm tự trừ). Ném lỗi nếu không đủ.</summary>
    Task<int> SpendAsync(string userId, int amount, string? referenceId, string? description, CancellationToken ct = default);

    /// <summary>Cấp gói Free (mặc định 10) cho tài khoản mới. Gọi lúc tạo user/student. Idempotent theo userId.</summary>
    Task GrantFreePackageAsync(string userId, CancellationToken ct = default);

    /// <summary>Tặng bonus (số lượt lấy từ system_configs) khi có booking. Idempotent theo bookingId.</summary>
    Task GrantBookingBonusAsync(string userId, int bookingId, CancellationToken ct = default);

    // Query
    Task<AiCreditBalanceResponse> GetBalanceAsync(string userId, CancellationToken ct = default);

    Task<IReadOnlyList<AiCreditTransactionResponse>> GetHistoryAsync(string userId, int take, CancellationToken ct = default);

    /// <summary>Danh sách gói cho client.</summary>
    Task<IReadOnlyList<AiCreditPackageResponse>> GetActivePackagesAsync(CancellationToken ct = default);

    //  Purchase
    /// <summary>Khởi tạo mua gói cho chính tài khoản đăng nhập (<paramref name="buyerUserId"/>):
    Task<AiCreditPurchaseResponse> InitiatePurchaseAsync(string buyerUserId, AiCreditPurchaseRequest request, CancellationToken ct = default);

    /// <summary>Hoàn tất mua gói khi PayOS báo thành công (gọi từ webhook).</summary>
    Task CompletePurchaseAsync(PaymentWebhookRequest webhook, string? rawPayload, CancellationToken ct = default);

    /// <summary>FE poll trạng thái đơn, tự cộng credit nếu PAID.</summary>
    Task<AiCreditPurchaseStatusResponse> GetPurchaseStatusAsync(string userId, long orderCode, CancellationToken ct = default);

    // Admin CRUD gói + config
    Task<IReadOnlyList<AiCreditPackageResponse>> AdminGetPackagesAsync(CancellationToken ct = default);
    Task<AiCreditPackageResponse> AdminCreatePackageAsync(AiCreditPackageCreateRequest request, CancellationToken ct = default);
    Task<AiCreditPackageResponse> AdminUpdatePackageAsync(int packageId, AiCreditPackageUpdateRequest request, CancellationToken ct = default);
    Task AdminDeletePackageAsync(int packageId, CancellationToken ct = default);

    Task<AiCreditPackageResponse> AdminUploadIconAsync(int packageId, Microsoft.AspNetCore.Http.IFormFile file, CancellationToken ct = default);

    /// <summary>Số lượt bonus/booking hiện tại (từ system_configs).</summary>
    Task<int> AdminGetBookingBonusAsync(CancellationToken ct = default);
    /// <summary>Đặt số lượt bonus/booking.</summary>
    Task AdminSetBookingBonusAsync(int amount, string? updatedByUserId, CancellationToken ct = default);
}
