namespace MV.DomainLayer.DTO.ResponseModel;

public class TutorClassAggregate
{
    public int BookingId { get; set; }
    public string? SubjectName { get; set; }
    public string? StudentName { get; set; }
    public int TotalSessions { get; set; }
    public int CompletedSessions { get; set; }
    public int ActiveSessions { get; set; }
    public bool HasInProgress { get; set; }
    public bool HasPending { get; set; }

    /// <summary>True if any session is still neither completed nor cancelled.</summary>
    public bool HasNonTerminal { get; set; }

    /// <summary>
    /// Số buổi còn ở trạng thái <c>reserved</c> — đã tạo sẵn lúc đặt lịch nhưng chưa mở, chờ
    /// phụ huynh trả nốt tiền. KHÔNG tính vào TotalSessions/NextSessionStart (buổi chưa mở thì
    /// chưa hứa với ai được), nhưng phải biết để không báo lớp "hoàn thành" khi còn buổi giữ chỗ.
    /// </summary>
    public int ReservedSessions { get; set; }

    /// <summary>Giờ bắt đầu của buổi giữ chỗ sớm nhất — chỉ để hiển thị lý do, không phải lịch chắc chắn.</summary>
    public DateTime? NextReservedStart { get; set; }

    /// <summary>Trạng thái booking (xem <c>BookingStatus</c>) — nguồn duy nhất quyết định "hoàn thành".</summary>
    public string? BookingStatus { get; set; }

    public DateTime? NextSessionStart { get; set; }
    public DateTime LatestStart { get; set; }
    public string? Schedule { get; set; }
}
