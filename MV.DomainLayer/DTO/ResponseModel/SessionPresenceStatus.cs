namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// Kết quả một nhịp heartbeat presence của phòng học: cho FE biết ai đang có mặt,
/// buổi đã được auto check-in chưa, phòng đã đóng chưa (đã check-out), và có bị chặn
/// bởi thanh toán đợt 2 không.
/// </summary>
/// <param name="TutorPresent">Gia sư đang trong phòng (heartbeat còn hiệu lực).</param>
/// <param name="StudentPresent">Học viên (hoặc phụ huynh thay thế) đang trong phòng.</param>
/// <param name="IsCheckedIn">Buổi học đã được điểm danh vào (đã có check-in).</param>
/// <param name="RoomClosed">Buổi đã check-out — phòng đóng, client nên rời.</param>
/// <param name="BlockedByPayment">Bị chặn auto check-in vì phụ huynh chưa thanh toán đợt 2.</param>
public record SessionPresenceStatus(
    bool TutorPresent,
    bool StudentPresent,
    bool IsCheckedIn,
    bool RoomClosed,
    bool BlockedByPayment
);
