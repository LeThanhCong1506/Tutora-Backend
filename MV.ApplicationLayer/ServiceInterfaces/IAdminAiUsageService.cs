using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel.Admin;

namespace MV.ApplicationLayer.ServiceInterfaces;

/// <summary>
/// Chi phí gọi Gemini: tutora-ai ghi vào, admin đọc thống kê ra.
/// Khác IAdminRevenueAnalyticsService.GetAiRevenueAsync (doanh thu bán credit).
/// </summary>
public interface IAdminAiUsageService
{
    /// <summary>Ghi nhận các lời gọi Gemini do tutora-ai đẩy về. Trả số bản ghi đã lưu.</summary>
    Task<int> IngestAsync(AiUsageIngestRequest request, CancellationToken ct = default);

    /// <summary>Tổng hợp cho dashboard: tổng kỳ, chuỗi ngày, gom theo model và feature.</summary>
    Task<AdminAiUsageResponse> GetUsageAsync(
        DateTime? from, DateTime? to, CancellationToken ct = default);

    /// <summary>Tỉ giá USD→VND đang dùng để quy đổi chi phí hiển thị.</summary>
    Task<AiUsageRateResponse> GetRateAsync(CancellationToken ct = default);

    /// <summary>Admin đặt tỉ giá thủ công. rate = null -> quay lại lấy tự động.</summary>
    Task<AiUsageRateResponse> SetRateAsync(decimal? rate, string? updatedByUserId, CancellationToken ct = default);
}
