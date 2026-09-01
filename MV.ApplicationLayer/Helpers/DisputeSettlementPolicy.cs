using MV.DomainLayer.Constants;
using static MV.DomainLayer.Constants.ClassSessionStatus;

namespace MV.ApplicationLayer.Helpers;

/// <summary>
/// Shared policy for opening a dispute without corrupting the booking settlement counter.
/// A settled class session has already reduced <c>Sessionsremaining</c>; reopening it must
/// restore exactly one unresolved session so the eventual admin resolution can settle it once.
/// </summary>
public static class DisputeSettlementPolicy
{
    public static bool IsTerminalBooking(string? bookingStatus)
        => bookingStatus is BookingStatus.Completed
            or BookingStatus.Cancelled
            or BookingStatus.CancelledNoshow
            or BookingStatus.CancelledByStaff
            or BookingStatus.CancelledByDispute;

    public static bool IsEligibleClassSession(string? classSessionStatus)
        => classSessionStatus is PendingConfirmation or Completed;

    /// <summary>
    /// CỜ TEST — bỏ mốc "đã tới giờ bắt đầu" để bấm thử nút khiếu nại trên buổi học còn ở tương lai,
    /// không phải ngồi chờ tới giờ. Đặt lại <c>false</c> khi test xong.
    ///
    /// Phải tắt trước khi lên production: bật nghĩa là khiếu nại được một buổi CHƯA diễn ra, và với
    /// loại no_show thì buổi bị chuyển thẳng sang NoShow — khoá luôn đường vào lớp của một buổi mà
    /// gia sư còn chưa có cơ hội dạy.
    /// </summary>
    public const bool AllowDisputeBeforeSessionStart = true;

    /// <summary>
    /// Như trên, nhưng buổi ĐANG ở <c>Scheduled</c> cũng hợp lệ một khi đã tới giờ bắt đầu.
    ///
    /// Trước đây khiếu nại chỉ mở sau khi gia sư nộp báo cáo (buổi mới sang PendingConfirmation),
    /// nên đúng tình huống cần khiếu nại gấp nhất — gia sư không xuất hiện — lại là tình huống
    /// không bao giờ có báo cáo để mở cổng. Người học phải đi đường riêng "Báo gia sư vắng mặt".
    ///
    /// Mốc là giờ BẮT ĐẦU chứ không phải lúc tạo buổi: trước giờ học thì chưa có gì để khiếu nại,
    /// và mở sớm chỉ tạo chỗ cho khiếu nại nhầm vào buổi chưa diễn ra.
    /// </summary>
    public static bool IsEligibleClassSession(
        string? classSessionStatus,
        DateTime scheduledStartUtc,
        DateTime nowUtc)
        => IsEligibleClassSession(classSessionStatus)
            || (classSessionStatus == Scheduled
                && (AllowDisputeBeforeSessionStart || nowUtc >= scheduledStartUtc));

    /// <summary>
    /// Buổi đang bị khiếu nại có THỰC SỰ diễn ra không — quyết định gia sư có được giữ tiền buổi đó
    /// khi admin chọn "Hủy khóa học &amp; hoàn tiền".
    ///
    /// Trước đây phương án này luôn xử buổi tranh chấp là đã dạy đủ. Giả định đó chỉ đúng với tranh
    /// chấp về CHẤT LƯỢNG (buổi có diễn ra, hai bên chỉ muốn dừng khoá). Với buổi gia sư vắng mặt,
    /// nó mâu thuẫn với chính luật của phương án — hoàn tiền các buổi CHƯA dạy — và trao cho gia sư
    /// tiền của một buổi không tồn tại. Trường hợp phụ huynh mới đóng cọc một buổi thì đúng khoản
    /// duy nhất họ đã trả bị tính hết cho buổi gia sư không tới, hoàn về 0đ.
    ///
    /// Dùng dữ liệu điểm danh đã ghi nhận, không hỏi admin: <c>Istutorpresent == false</c> là ghi
    /// nhận DỨT KHOÁT rằng gia sư vắng (null = chưa ghi nhận gì, không tính).
    /// </summary>
    public static bool TutorWasAbsent(string? classSessionStatus, bool? isTutorPresent)
        => classSessionStatus == NoShow || isTutorPresent == false;

    /// <summary>
    /// Phần trăm hoàn cho chính buổi đang khiếu nại khi chọn "Hủy khóa học &amp; hoàn tiền":
    /// gia sư vắng mặt thì hoàn trọn buổi đó, còn lại giữ nguyên luật cũ (gia sư giữ tiền buổi đã dạy).
    /// </summary>
    public static int CancelCourseRefundPercentage(string? classSessionStatus, bool? isTutorPresent)
        => TutorWasAbsent(classSessionStatus, isTutorPresent) ? 100 : 0;

    public static int SessionsRemainingAfterOpeningDispute(int? currentSessionsRemaining, bool wasSettled)
        => wasSettled
            ? checked(Math.Max(0, currentSessionsRemaining ?? 0) + 1)
            : Math.Max(0, currentSessionsRemaining ?? 0);
}
