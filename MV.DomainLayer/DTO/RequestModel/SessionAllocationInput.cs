using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel;

/// <summary>
/// Một ô tick trong bảng "Hủy khóa học &amp; hoàn tiền": Admin/Staff chỉ định buổi này được tính cho
/// bên nào. Không có trạng thái "không chọn" — mọi buổi chưa settle đều phải được phân bổ, nếu không
/// tiền của buổi đó kẹt vĩnh viễn trong escrow (booking đã đóng thì không còn đường nào giải phóng).
/// </summary>
public class SessionAllocationInput
{
    [Required]
    public int ClassSessionId { get; set; }

    /// <summary>
    /// <c>"tutor"</c> = buổi được tính là đã dạy, giải ngân cho gia sư.
    /// <c>"parent"</c> = buổi được tính là chưa dạy, hoàn cho phụ huynh.
    /// Xem <see cref="MV.DomainLayer.DTO.ResponseModel.SessionAllocations"/>.
    /// </summary>
    [Required]
    public string Allocation { get; set; } = null!;
}
