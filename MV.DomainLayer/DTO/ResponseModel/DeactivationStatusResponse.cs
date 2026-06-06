namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// Kết quả trả về sau khi người dùng toggle trạng thái tự khóa/mở tài khoản.
/// </summary>
public class DeactivationStatusResponse
{
    /// <summary>Trạng thái hiện tại sau khi toggle: true = đang khóa, false = đang hoạt động.</summary>
    public bool IsDeactivated { get; set; }

    /// <summary>Thời điểm thay đổi trạng thái (theo giờ Việt Nam).</summary>
    public DateTime? DeactivatedAt { get; set; }

    /// <summary>Thông báo hiển thị cho người dùng.</summary>
    public string Message { get; set; } = string.Empty;
}
