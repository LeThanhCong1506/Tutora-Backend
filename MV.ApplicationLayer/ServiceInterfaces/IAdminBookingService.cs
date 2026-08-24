using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel.Admin;

namespace MV.ApplicationLayer.ServiceInterfaces;

public interface IAdminBookingService
{
    /// <summary>
    /// Returns a paged list of all bookings across the platform for admin review.
    /// Supports filtering by status, teaching mode, tutor, parent, subject, date range,
    /// keyword search, booking id, class session id, and ordering by creation time.
    /// Each item includes party info (tutor/parent/student), financial summary, session progress, and classSession counts.
    /// </summary>
    /// <param name="query">Filter, sort and paging parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<AdminBookingListResponse> GetAdminBookingsAsync(
        AdminBookingQueryRequest query,
        CancellationToken ct = default);

    /// <summary>
    /// Returns full detail of a single booking by id for admin review.
    /// No ownership check — admin can view any booking.
    /// Returns null when bookingId does not exist.
    /// </summary>
    Task<AdminBookingDetailResponse?> GetAdminBookingDetailAsync(int bookingId, CancellationToken ct = default);

    /// <summary>
    /// Staff hủy booking sau khi xác minh NGOÀI hệ thống (qua tổng đài) rằng phụ huynh đã "nghỉ
    /// ngang". Giải ngân toàn bộ escrow còn lại cho gia sư. Trả về false nếu booking không tồn
    /// tại hoặc không ở trạng thái hợp lệ để hủy (đã terminal, hoặc có buổi đang mid-flight).
    /// </summary>
    Task<bool> CancelGhostBookingAsync(int bookingId, string adminId, string reason, CancellationToken ct = default);
}
