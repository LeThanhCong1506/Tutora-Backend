using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces;

/// <summary>
/// Service interface for parent lesson management
/// </summary>
public interface IParentService
{
    /// <summary>
    /// Get lessons pending parent/student confirmation
    /// </summary>
    Task<List<PendingLessonResponse>> GetPendingLessonsAsync(string userId, string role);

    /// <summary>
    /// Get lesson detail for parent/student view
    /// </summary>
    Task<LessonDetailResponse?> GetLessonDetailAsync(int lessonId, string userId, string role);

    /// <summary>
    /// Confirm a lesson as completed (triggers settlement)
    /// </summary>
    Task<SettlementResultResponse> ConfirmLessonAsync(int lessonId, string userId, string role);

    /// <summary>
    /// Create a dispute for a lesson
    /// </summary>
    Task<DisputeDetailResponse> CreateDisputeAsync(int lessonId, string userId, string role, CreateDisputeRequest request);

    /// <summary>
    /// Get parent's/student's dispute history
    /// </summary>
    Task<PagedList<DisputeListResponse>> GetParentDisputesAsync(string userId, string role, int page, int pageSize);

    /// <summary>
    /// Get calendar view
    /// </summary>
    Task<List<CalendarDayResponse>> GetParentCalendarAsync(string userId, string role, DateTime startDate, DateTime endDate);
}
