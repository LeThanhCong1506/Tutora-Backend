namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// Trạng thái đủ điều kiện đặt lịch của học sinh.
/// </summary>
public class StudentBookingEligibilityResponse
{
    /// <summary>Có được phép đặt lịch hay không.</summary>
    public bool CanBook { get; set; }

    /// <summary>Mã lý do khi CanBook=false. Xem BookingErrorCodes.</summary>
    public string? ReasonCode { get; set; }

    /// <summary>Lý do hiển thị cho người dùng khi CanBook=false.</summary>
    public string? Reason { get; set; }

    /// <summary>Tài khoản do phụ huynh tạo/quản lý — phụ huynh mới được đặt lịch.</summary>
    public bool IsParentManaged { get; set; }

    /// <summary>Cần hoàn thiện thông tin hồ sơ.</summary>
    public bool NeedProfile { get; set; }

    /// <summary>Cần xác minh CCCD (chưa xác minh độ tuổi).</summary>
    public bool NeedAgeVerification { get; set; }

    /// <summary>Đã nộp CCCD nhưng OCR tự động thất bại nhiều lần — đang chờ Admin xem thủ công, KHÔNG
    /// cần nộp lại. Luôn đi kèm NeedAgeVerification = true (vẫn chưa xác minh xong).</summary>
    public bool IsPendingIdentityReview { get; set; }

    /// <summary>Đã xác minh nhưng chưa đủ 16 tuổi.</summary>
    public bool IsUnderage { get; set; }

    /// <summary>Tuổi hiện tại nếu có.</summary>
    public int? Age { get; set; }
}
