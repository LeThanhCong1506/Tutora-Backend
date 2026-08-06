using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.DTO.ResponseModel.Admin;

namespace MV.ApplicationLayer.ServiceInterfaces;

/// <summary>
/// Service interface for feedback and rating management
/// </summary>
public interface IFeedbackService
{
    /// <summary>
    /// Create the review for a completed booking
    /// </summary>
    Task<FeedbackListResponse> CreateFeedbackAsync(string fromUserId, CreateFeedbackRequest request);

    /// <summary>
    /// Update an existing booking review (author only, before the tutor replies)
    /// </summary>
    Task<FeedbackListResponse> UpdateFeedbackAsync(int feedbackId, string userId, UpdateFeedbackRequest request);

    /// <summary>
    /// Get the review of a booking for its own reviewer. Null when not reviewed yet.
    /// </summary>
    Task<FeedbackListResponse?> GetBookingFeedbackAsync(int bookingId, string userId);

    /// <summary>
    /// Get feedbacks for a tutor (public view)
    /// </summary>
    Task<PagedList<FeedbackListResponse>> GetTutorFeedbacksAsync(string tutorId, int page, int pageSize);

    /// <summary>
    /// Get feedbacks for CMS moderation — includes hidden ones
    /// </summary>
    Task<AdminFeedbackListResponse> GetFeedbacksForAdminAsync(
        string? tutorId, int? rating, bool? isVisible, int page, int pageSize);

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
    /// <param name="reason">Bắt buộc khi ẩn, bỏ qua khi hiện lại.</param>
    Task<bool> ToggleFeedbackVisibilityAsync(int feedbackId, string adminId, string? reason = null);

    /// <summary>
    /// Recalculate tutor's average rating
    /// </summary>
    Task RecalculateTutorRatingAsync(string tutorId);

    /// <summary>
    /// Check if user can review a booking
    /// </summary>
    Task<bool> CanLeaveBookingFeedbackAsync(int bookingId, string userId);
}
