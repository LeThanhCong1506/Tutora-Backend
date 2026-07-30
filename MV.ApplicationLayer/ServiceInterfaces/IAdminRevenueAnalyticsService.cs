using MV.DomainLayer.DTO.ResponseModel.Admin;

namespace MV.ApplicationLayer.ServiceInterfaces;

public interface IAdminRevenueAnalyticsService
{
    Task<AdminRevenueOverviewResponse> GetOverviewAsync(
        DateTime? from, DateTime? to, CancellationToken ct = default);

    Task<AdminRevenueRecognitionResponse> GetRecognitionAsync(
        DateTime? from, DateTime? to, CancellationToken ct = default);

    Task<AdminTutorRevenueResponse> GetTutorRevenueAsync(
        DateTime? from, DateTime? to, int top, CancellationToken ct = default);

    Task<AdminCustomerRevenueResponse> GetCustomerRevenueAsync(
        DateTime? from, DateTime? to, int top, CancellationToken ct = default);

    Task<AdminSubjectRevenueResponse> GetSubjectRevenueAsync(
        DateTime? from, DateTime? to, CancellationToken ct = default);

    Task<AdminAiRevenueResponse> GetAiRevenueAsync(
        DateTime? from, DateTime? to, int top, CancellationToken ct = default);
}
