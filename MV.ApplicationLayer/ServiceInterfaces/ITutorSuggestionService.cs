using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces;

/// <summary>
/// Gợi ý gia sư dựa trên chương mà học sinh đang vướng trong phiên giải bài.
/// </summary>
public interface ITutorSuggestionService
{
    /// <summary>
    /// Gợi ý cho một phiên chat cụ thể.
    /// </summary>
    Task<StudentTutorSuggestionResponse> GetForSessionAsync(
        string userId, Guid? sessionId, CancellationToken ct = default);

    /// <summary>Học sinh bật/tắt nhận gợi ý.</summary>
    Task SetUserPreferenceAsync(string userId, bool enabled, CancellationToken ct = default);

    Task VoteTutorAsync(string userId, TutorSuggestionVoteRequest dto, CancellationToken ct = default);

    // Cấu hình admin (CMS)

    Task<TutorSuggestionSettingsResponse> GetSettingsAsync(CancellationToken ct = default);

    Task<TutorSuggestionSettingsResponse> UpdateSettingsAsync(
        TutorSuggestionSettingsRequest dto, string? updatedByUserId, CancellationToken ct = default);
}
