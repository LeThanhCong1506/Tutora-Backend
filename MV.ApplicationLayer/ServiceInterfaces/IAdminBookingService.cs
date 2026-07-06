using MV.DomainLayer.DTO.ResponseModel.Admin;

namespace MV.ApplicationLayer.ServiceInterfaces;

public interface IAdminBookingService
{
    /// <summary>
    /// Returns a paged list of all bookings across the platform for admin review.
    /// Supports filtering by status, teaching mode, tutor, parent, subject, date range, and keyword search.
    /// Each item includes party info (tutor/parent/student), financial summary, session progress, and classSession counts.
    /// </summary>
    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Items per page (max 100).</param>
    /// <param name="status">Filter by BookingStatus constant. Null = all.</param>
    /// <param name="teachingMode">Filter by teaching mode (online/offline/hybrid). Null = all.</param>
    /// <param name="tutorId">Filter by tutor user id. Null = all.</param>
    /// <param name="parentId">Filter by parent user id. Null = all.</param>
    /// <param name="subjectId">Filter by subject id. Null = all.</param>
    /// <param name="from">Filter bookings created on or after this UTC datetime. Null = no lower bound.</param>
    /// <param name="to">Filter bookings created on or before this UTC datetime. Null = no upper bound.</param>
    /// <param name="search">Case-insensitive keyword matched against tutor name, parent name, or payment code. Null = no filter.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<AdminBookingListResponse> GetAdminBookingsAsync(
        int page,
        int pageSize,
        string? status,
        string? teachingMode,
        string? tutorId,
        string? parentId,
        int? subjectId,
        DateTime? from,
        DateTime? to,
        string? search,
        CancellationToken ct = default);

    /// <summary>
    /// Returns full detail of a single booking by id for admin review.
    /// No ownership check — admin can view any booking.
    /// Returns null when bookingId does not exist.
    /// </summary>
    Task<AdminBookingDetailResponse?> GetAdminBookingDetailAsync(int bookingId, CancellationToken ct = default);
}
