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

    /// <summary>
    /// Học sinh CHỌN được gia sư và khung giờ, nhưng không tự thanh toán: booking dừng ở
    /// pending_payment và hiện trong danh sách của phụ huynh để duyệt và trả tiền.
    /// Đi kèm <see cref="CanBook"/> = true và <see cref="IsParentManaged"/> = true.
    /// </summary>
    public bool RequiresParentPayment { get; set; }

    /// <summary>Cần hoàn thiện thông tin hồ sơ.</summary>
    public bool NeedProfile { get; set; }

    /// <summary>Cần xác minh CCCD (chưa xác minh độ tuổi).</summary>
    public bool NeedAgeVerification { get; set; }

    /// <summary>Đã xác minh nhưng chưa đủ 16 tuổi.</summary>
    public bool IsUnderage { get; set; }

    /// <summary>Tuổi hiện tại nếu có.</summary>
    public int? Age { get; set; }
}
