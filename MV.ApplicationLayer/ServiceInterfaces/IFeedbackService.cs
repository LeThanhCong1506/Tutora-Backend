using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces;

/// <summary>
/// Service interface for feedback and rating management
/// </summary>
public interface IFeedbackService
{
    /// <summary>
    /// Create feedback for a lesson
    /// </summary>
    Task<FeedbackListResponse> CreateFeedbackAsync(string fromUserId, CreateFeedbackRequest request);

    /// <summary>
    /// Get feedbacks for a tutor (public view)
    /// </summary>
    Task<PagedList<FeedbackListResponse>> GetTutorFeedbacksAsync(string tutorId, int page, int pageSize);

    /// <summary>
    /// Get feedback statistics for a tutor
    /// </summary>
    Task<FeedbackStatsResponse> GetTutorFeedbackStatsAsync(string tutorId);

    /// <summary>
    /// Tutor reply to a feedback
    /// </summary>
    Task<FeedbackListResponse> ReplyFeedbackAsync(int feedbackId, string tutorId, ReplyFeedbackRequest request);

    /// <summary>
    /// Admin toggle feedback visibility
    /// </summary>
    Task<bool> ToggleFeedbackVisibilityAsync(int feedbackId, string adminId);

    /// <summary>
    /// Recalculate tutor's average rating
    /// </summary>
    Task RecalculateTutorRatingAsync(string tutorId);

    /// <summary>
    /// Check if user can leave feedback for lesson
    /// </summary>
    Task<bool> CanLeaveFeedbackAsync(int lessonId, string userId);

    /// <summary>
    /// Check if user can leave early termination feedback for booking
    /// </summary>
    Task<bool> CanLeaveBookingFeedbackAsync(int bookingId, string userId);
}
