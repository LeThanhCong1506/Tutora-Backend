using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces;

public interface IBookingService
{
    /// <summary>
    /// Create a new booking between a student/parent and a tutor.
    /// </summary>
    Task<BookingResponse> CreateBookingAsync(string userId, string userRole, CreateBookingRequest dto);

    /// <summary>
    /// Paged booking list — role-aware: returns own bookings for student/parent/tutor.
    /// Optionally filtered by <paramref name="status"/>.
    /// </summary>
    Task<PagedList<BookingResponse>> GetMyBookingsAsync(string userId, string userRole, int page, int pageSize, string? status = null);

    /// <summary>
    /// Single booking by id — ownership-checked for the calling user's role.
    /// </summary>
    Task<BookingResponse?> GetBookingByIdAsync(int id, string userId, string userRole);

    /// <summary>
    /// Validate a promotion code without applying it; returns discount info if the code is valid.
    /// </summary>
    Task<PromotionValidateResponse> ValidatePromotionAsync(string code, decimal orderValue);

    /// <summary>
    /// Apply a promotion code to an existing pending booking.
    /// </summary>
    Task<BookingResponse?> ApplyPromotionAsync(int bookingId, string userId, string promotionCode);

    /// <summary>
    /// Cancel a booking; only the booking owner may cancel, with an optional reason.
    /// </summary>
    Task<bool> CancelBookingAsync(int bookingId, string userId, string? reason = null);

    /// <summary>
    /// Admin creates a promotion (discount code) for the platform.
    /// </summary>
    Task<PromotionCreatedResponse> CreatePromotionAsync(CreatePromotionRequest dto);

    /// <summary>
    /// Admin: paged list of all promotions with optional filters.
    /// </summary>
    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Items per page (max 100).</param>
    /// <param name="isActive">Filter by active flag. Null = all.</param>
    /// <param name="search">Keyword matched against Code or Description (case-insensitive). Null = no filter.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<PromotionListResponse> GetAllPromotionsAsync(
        int page,
        int pageSize,
        bool? isActive,
        string? search,
        CancellationToken ct = default);

    /// <summary>
    /// Admin: update an existing promotion. Only non-null fields in the request are applied (PATCH semantics).
    /// Code cannot be changed after creation.
    /// </summary>
    /// <param name="promotionId">Id of the promotion to update.</param>
    /// <param name="dto">Fields to update — null fields are ignored.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Updated promotion detail, or null if not found.</returns>
    Task<PromotionResponse?> UpdatePromotionAsync(int promotionId, UpdatePromotionRequest dto, CancellationToken ct = default);

    /// <summary>
    /// Admin: soft-delete a promotion by setting IsActive = false.
    /// Promotions that are already linked to pending/active bookings can still be deactivated
    /// because the discount was already applied at booking creation time.
    /// Returns false if the promotion does not exist.
    /// </summary>
    /// <param name="promotionId">Id of the promotion to deactivate.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<(bool Found, bool AlreadyInactive)> DeactivatePromotionAsync(int promotionId, CancellationToken ct = default);

    /// <summary>
    /// Tutor accepts an incoming booking request; triggers automatic classSession creation.
    /// </summary>
    Task<TutorDecisionResponse> AcceptBookingAsync(string tutorId, int bookingId);

    /// <summary>
    /// Tutor declines an incoming booking request with an optional reason.
    /// </summary>
    Task<BookingResponse> DeclineBookingAsync(string tutorId, int bookingId, string? reason);

    /// <summary>
    /// Paged list of booking requests addressed to a tutor, optionally filtered by status.
    /// </summary>
    Task<PagedList<BookingResponse>> GetTutorBookingRequestsAsync(string tutorId, int page, int pageSize, string? status = null);

    /// <summary>
    /// Returns the absolute occupied time ranges for a tutor within the requested window.
    /// Used by FE to disable/gray-out occupied slots before the user submits a booking.
    /// </summary>
    Task<List<BookedSlotResponse>> GetTutorBookedSlotsAsync(string tutorId, DateTime startDate, DateTime endDate);
}
