namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// Ai được nhận tiền của một buổi khi Admin/Staff hủy khóa học.
/// </summary>
public static class SessionAllocations
{
    /// <summary>Buổi được tính là ĐÃ DẠY — giải ngân cho gia sư, phụ huynh không được hoàn.</summary>
    public const string Tutor = "tutor";

    /// <summary>Buổi được tính là CHƯA DẠY — hoàn cho phụ huynh, gia sư không nhận.</summary>
    public const string Parent = "parent";

    /// <summary>Chưa quyết — Admin/Staff bắt buộc phải chọn trước khi xác nhận phương án.</summary>
    public const string None = "none";

    public static readonly string[] Selectable = { Tutor, Parent };
}

/// <summary>
/// Một dòng trong bảng "Hủy khóa học &amp; hoàn tiền": một buổi của khóa, kèm bằng chứng có mặt và
/// số tiền mỗi bên sẽ nhận nếu Admin/Staff tick vào ô tương ứng.
///
/// Hai số tiền KHÔNG bằng nhau và đó là chủ ý: gia sư nhận giá gốc đã trừ phí sàn, còn phụ huynh
/// được hoàn giá gốc — phí dịch vụ 5% chỉ hoàn khi khóa chưa qua đợt thanh toán thứ hai. Xem
/// <see cref="CourseCancelPreviewResponse.RefundIncludesServiceFee"/>.
/// </summary>
public class CancelPreviewSessionRow
{
    public int ClassSessionId { get; set; }

    /// <summary>Thứ tự buổi trong khóa (1-based), sắp theo <c>Scheduledstart</c>.</summary>
    public int SessionNumber { get; set; }

    public DateTime ScheduledStart { get; set; }

    public string? Status { get; set; }

    /// <summary>True với đúng buổi đang bị khiếu nại — giao diện tô sáng dòng này.</summary>
    public bool IsDisputedSession { get; set; }

    /// <summary>
    /// True khi buổi đã được CHỐT: phụ huynh xác nhận xong, buổi về Completed và Issettled = true.
    ///
    /// KHÔNG có nghĩa là tiền đã ra khỏi escrow — escrow chỉ được giải phóng khi cả booking hoàn
    /// tất (xem <c>ReleaseEscrowIfBookingCompleteAsync</c>). Nhưng phần thuộc về gia sư thì đã được
    /// định đoạt, nên buổi này LUÔN tính cho gia sư và Admin/Staff không đổi được — bỏ nó ra khỏi
    /// phép chia sẽ khiến gia sư mất tiền của một buổi đã dạy xong và đã được xác nhận.
    /// </summary>
    public bool IsAlreadySettled { get; set; }

    /// <summary>
    /// True khi buổi đã bị hủy từ trước. Tiền của nó đã được xử lý hoặc chưa từng thu, nên không
    /// thuộc về bên nào trong lần chia này — bắt Admin/Staff tick cho nó là ép họ phân bổ tiền cho
    /// một buổi không còn tồn tại.
    /// </summary>
    public bool IsCancelled { get; set; }

    /// <summary>
    /// Admin/Staff có được chọn bên nhận cho buổi này không. False = đã có kết quả cố định (buổi
    /// đã chốt luôn thuộc về gia sư; buổi đã hủy không thuộc về ai), giao diện khoá dòng và kiểm
    /// tra phía backend cũng không đòi tick.
    /// </summary>
    public bool IsAllocatable => !IsAlreadySettled && !IsCancelled;

    /// <summary>Giây gia sư thực sự ở trong phòng học (dữ liệu Agora), null khi buổi chưa từng mở.</summary>
    public int? TutorSeconds { get; set; }

    /// <summary>Giây học sinh thực sự ở trong phòng học.</summary>
    public int? StudentSeconds { get; set; }

    /// <summary>Giây hai bên cùng có mặt — con số quyết định "buổi này có diễn ra không".</summary>
    public int? OverlapSeconds { get; set; }

    /// <summary>
    /// False khi không có bằng chứng có mặt nào (cả Agora lẫn heartbeat trình duyệt): giao diện
    /// hiển thị gạch ngang thay vì "0 phút" — hai chuyện đó khác hẳn nhau khi phân xử tiền.
    /// </summary>
    public bool HasAttendanceData { get; set; }

    /// <summary>
    /// False khi số liệu chưa đủ chắc để tự nó kết luận (ví dụ Agora không gửi dữ liệu, chỉ còn
    /// heartbeat từ trình duyệt người dùng). Vẫn hiển thị thời lượng, nhưng phải báo cho Admin/Staff
    /// biết đây là bằng chứng yếu hơn trước khi họ tick chia tiền.
    /// </summary>
    public bool IsEvidenceConclusive { get; set; }

    /// <summary>Số tiền gia sư nhận nếu tick ô gia sư (giá gốc đã trừ phí sàn).</summary>
    public decimal TutorAmount { get; set; }

    /// <summary>Số tiền phụ huynh được hoàn nếu tick ô phụ huynh.</summary>
    public decimal ParentAmount { get; set; }

    /// <summary>
    /// Ô được tick sẵn khi mở trang: <see cref="SessionAllocations.Parent"/> cho buổi chưa học,
    /// <see cref="SessionAllocations.None"/> cho buổi đã học — buổi đã học để trống có chủ ý, buộc
    /// Admin/Staff đọc bằng chứng có mặt rồi tự quyết thay vì bấm xác nhận theo quán tính.
    /// </summary>
    public string DefaultAllocation { get; set; } = SessionAllocations.None;
}
