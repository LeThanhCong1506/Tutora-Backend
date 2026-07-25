using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces;

public interface IClassSessionScheduleChangeService
{
    Task<SessionScheduleChangeResponse> GetExistingStateAsync(
        int classSessionId,
        string userId,
        string? role,
        CancellationToken cancellationToken = default);

    Task<SessionScheduleChangeResponse> GetOrCreateStateAsync(
        int classSessionId,
        string userId,
        string? role,
        CancellationToken cancellationToken = default);

    Task<SessionScheduleChangeResponse> RespondAsync(
        int classSessionId,
        string userId,
        string? role,
        bool confirmed,
        CancellationToken cancellationToken = default);
}
