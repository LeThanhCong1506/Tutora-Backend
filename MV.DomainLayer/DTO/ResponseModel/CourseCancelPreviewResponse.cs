namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// Dry-run preview cho resolution "cancel_course" (case 4 — hủy toàn bộ các buổi chưa diễn ra của
/// một booking, hoàn cho phụ huynh theo giá gốc không gồm phí dịch vụ). Không có side effect.
/// </summary>
public class CourseCancelPreviewResponse
{
    public int BookingId { get; set; }

    /// <summary>Số buổi Scheduled/Reserved sẽ bị hủy nếu resolve bằng cancel_course.</summary>
    public int RemainingSessionsCount { get; set; }

    /// <summary>Số tiền phụ huynh sẽ được hoàn (giá gốc mỗi buổi, không gồm 5% phí dịch vụ) — đã trừ
    /// phần thuộc về gia sư cho (các) buổi đã dạy, nên không bao giờ cộng với
    /// <see cref="TutorEscrowReleased"/> vượt quá số tiền phụ huynh thực đã trả.</summary>
    public decimal ParentRefundAmount { get; set; }

    /// <summary>Số buổi được tính là ĐÃ dạy (Completed/Issettled, kể cả buổi đang khiếu nại — buổi đó
    /// sẽ được settle như đã dạy đủ trước khi hủy phần còn lại nếu resolve bằng cancel_course).</summary>
    public int DeliveredSessionsCount { get; set; }

    /// <summary>Số tiền sẽ GIẢI NGÂN cho gia sư (Frozenbalance → Balance) cho các buổi đã dạy.</summary>
    public decimal TutorEscrowReleased { get; set; }

    /// <summary>Số tiền escrow của gia sư sẽ bị rút lại cho các buổi CHƯA dạy (không phải hoàn cho ai
    /// — chỉ là giải phóng đóng băng, khác với <see cref="TutorEscrowReleased"/>).</summary>
    public decimal TutorEscrowReversed { get; set; }

    public decimal TutorFrozenBalance { get; set; }

    /// <summary>Non-empty khi số tiền hoàn thực tế bị giới hạn (đã thu ít hơn dự kiến, hoặc escrow không đủ).</summary>
    public List<string> Warnings { get; set; } = new();
}
