using MV.DomainLayer.Constants;

namespace MV.ApplicationLayer.Helpers;

/// <summary>
/// Quyết định sản phẩm: 1 khung giờ chỉ thực sự "khóa" (chặn người khác đặt) khi gia sư đã CHỦ ĐỘNG
/// accept 1 booking cho khung giờ đó (deposit_paid trở lên). Booking mới tạo (pending_payment)
/// hoặc đã đóng cọc nhưng gia sư chưa quyết định (pending_tutor) KHÔNG khóa khung giờ — nhiều
/// người có thể cùng đặt cọc cho 1 khung giờ, gia sư accept ai thì người còn lại tự động bị hủy +
/// hoàn tiền (xem BookingService.TutorDecisions.cs).
/// </summary>
public static class BookingScheduleLockPolicy
{
    private static readonly HashSet<string> LockedStatuses =
    [
        BookingStatus.Accepted, // legacy, không còn được gán mới nhưng dữ liệu cũ có thể còn — coi như đã khóa cho an toàn.
        BookingStatus.DepositPaid,
        BookingStatus.PendingRemainingPayment,
        BookingStatus.Paid,
        BookingStatus.Ongoing,
        BookingStatus.Completed,
    ];

    /// <summary>True nếu booking ở trạng thái này thực sự khóa khung giờ (gia sư đã accept).</summary>
    public static bool IsLockingStatus(string? bookingStatus)
        => bookingStatus != null && LockedStatuses.Contains(bookingStatus);

    /// <summary>True nếu booking đang cạnh tranh khung giờ (đã tạo/đã đóng cọc nhưng gia sư chưa
    /// quyết định) — chưa khóa, nhưng sẽ tự động bị hủy nếu gia sư accept 1 booking khác trùng giờ.</summary>
    public static bool IsCompetingStatus(string? bookingStatus)
        => bookingStatus == BookingStatus.PendingTutor || bookingStatus == BookingStatus.PendingPayment;
}
