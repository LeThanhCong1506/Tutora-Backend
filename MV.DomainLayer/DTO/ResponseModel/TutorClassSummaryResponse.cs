namespace MV.DomainLayer.DTO.ResponseModel;

public class TutorClassListResponse
{
    public List<TutorClassSummaryResponse> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public class TutorClassSummaryResponse
{
    public int BookingId { get; set; }
    public string? SubjectName { get; set; }
    public string? StudentName { get; set; }

    /// <summary>Total non-cancelled sessions in the booking (the "X buổi" tag).</summary>
    public int TotalSessions { get; set; }

    /// <summary>Sessions with status <c>completed</c>.</summary>
    public int CompletedSessions { get; set; }

    /// <summary>Distinct weekday+time slots, e.g. "T2 18:05, T3 14:30" (max 3).</summary>
    public string? Schedule { get; set; }

    /// <summary>Start of the next upcoming session, or null when none remain.</summary>
    public DateTime? NextSessionStart { get; set; }

    /// <summary>
    /// Số buổi đã tạo sẵn nhưng CHƯA mở (<c>reserved</c>) — chờ phụ huynh trả nốt tiền. Không nằm
    /// trong TotalSessions. Dùng để giải thích vì sao không có buổi kế tiếp.
    /// </summary>
    public int ReservedSessions { get; set; }

    /// <summary>Giờ dự kiến của buổi giữ chỗ sớm nhất — chỉ để hiển thị lý do, chưa phải lịch chắc chắn.</summary>
    public DateTime? NextReservedStart { get; set; }

    /// <summary>Trạng thái booking (xem <c>BookingStatus</c>).</summary>
    public string? BookingStatus { get; set; }

    /// <summary>Derived class status: scheduled | in_progress | pending_confirmation | reserved | completed.</summary>
    public string Status { get; set; } = "unknown";
}
