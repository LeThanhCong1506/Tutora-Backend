namespace MV.DomainLayer.Configuration;

/// <summary>
/// Các mốc thời gian của luồng xử lý buổi học không ai vào lớp.
///
/// Giá trị mặc định ở đây LÀ giá trị nghiệp vụ thật — không cần khai báo gì trong appsettings để
/// chạy production. Tách ra config chỉ để test end-to-end trên client mà không phải chờ nửa ngày:
/// hạ xuống vài phút trong appsettings.Development.json là đặt lịch một buổi, bỏ mặc nó, rồi xem
/// cả chuỗi thông báo → cửa sổ xác nhận → auto-confirm diễn ra trong ít phút.
/// </summary>
public class AbandonedSessionSettings
{
    public const string SectionName = "AbandonedSession";

    /// <summary>
    /// Chờ bao lâu sau giờ kết thúc rồi mới coi buổi là "đã trôi qua". Đủ dài để các luồng bình
    /// thường (gia sư nộp báo cáo muộn, phụ huynh tự báo cáo vắng mặt) kịp chạy trước, đủ ngắn để
    /// hai bên còn nhớ chuyện gì đã xảy ra khi nhận thông báo.
    /// </summary>
    public double NoticeDelayHours { get; set; } = 6;

    /// <summary>
    /// Cửa sổ để hai bên phản hồi trước khi buổi tự về luồng xác nhận. Cố tình lệch khỏi
    /// <c>DisputeService.TutorResponseGraceHours</c> (48h, cho luồng dispute cần gia sư phản hồi) —
    /// ở đây trùng <c>ConfirmWindowHours</c>/<c>ClassSessionService.M3.NoShow.ConfirmWindowHours</c>
    /// (12h) vì đây cũng là 1 cửa sổ xác nhận buổi học, không phải cửa sổ tranh chấp.
    /// </summary>
    public double ResponseWindowHours { get; set; } = 12;

    /// <summary>
    /// Thời gian tối thiểu ở phòng chờ để tính là "đã đến". Sự tồn tại của một lượt lobby KHÔNG
    /// nói lên gì — mở ra liếc vài giây rồi đóng cũng tạo ra một dòng y hệt một lượt chờ thật.
    ///
    /// Đổi giá trị này thì phải sửa cả hằng số ATTENDANCE_MINUTES trong AttendanceTimer.tsx
    /// (Tutora-FE), nếu không đồng hồ đếm hiện cho người dùng sẽ nói sai.
    /// </summary>
    public double LobbyPresenceMinimumMinutes { get; set; } = 3;

    /// <summary>Nhịp quét của job. Hạ xuống khi test để khỏi chờ hết một chu kỳ.</summary>
    public double ScanIntervalMinutes { get; set; } = 30;
}
