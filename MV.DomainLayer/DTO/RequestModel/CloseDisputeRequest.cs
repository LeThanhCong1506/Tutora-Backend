using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel;

/// <summary>
/// Admin đóng tranh chấp khi hai bên đã tự dàn xếp với nhau và muốn học tiếp — khác với
/// <see cref="ResolveDisputeRequest"/> ở chỗ không có bên nào thắng/thua và không có quyết định
/// hoàn tiền. Tiền buổi học đi theo <see cref="ClassSessionOutcome"/>.
/// </summary>
public class CloseDisputeRequest
{
    /// <summary>
    /// Buổi học bị phản ánh sẽ về trạng thái nào — xem <see cref="CloseDisputeOutcomes"/>.
    /// Bắt buộc vì buổi học đang ở trạng thái "disputed" sẽ kẹt vĩnh viễn nếu không đặt lại:
    /// SettlementService từ chối quyết toán mọi buổi không ở pending_confirmation/completed.
    /// </summary>
    [Required(ErrorMessage = "Trạng thái buổi học là bắt buộc")]
    public string ClassSessionOutcome { get; set; } = null!;

    /// <summary>Ghi chú nội dung hai bên đã thoả thuận — lưu vào Resolutionnote của tranh chấp.</summary>
    [Required(ErrorMessage = "Ghi chú là bắt buộc")]
    [StringLength(2000, MinimumLength = 10, ErrorMessage = "Ghi chú phải từ 10 đến 2000 ký tự")]
    public string Note { get; set; } = null!;

    /// <summary>Giờ học lại do Admin/Staff chọn khi hai bên đã thống nhất — bắt buộc khi
    /// <see cref="ClassSessionOutcome"/> là <see cref="CloseDisputeOutcomes.Reschedule"/>. Đây là
    /// giờ cho buổi học lại MỚI (link 3), không sửa giờ của buổi gốc đang tranh chấp.
    /// Validation bắt buộc theo điều kiện này được thêm ở service, không phải DataAnnotation ở đây.</summary>
    public DateTime? RelearnScheduledStart { get; set; }
}

/// <summary>Trạng thái buổi học sau khi admin đóng tranh chấp do hai bên hoà giải.</summary>
public static class CloseDisputeOutcomes
{
    /// <summary>Buổi học vẫn tính là đã dạy — quyết toán cho gia sư như bình thường, không hoàn tiền.</summary>
    public const string Completed = "completed";

    /// <summary>Hai bên thống nhất học lại buổi này — trả buổi về "scheduled", xoá dấu vết điểm danh
    /// của lần trước và KHÔNG quyết toán (tiền vẫn nằm trong booking để dùng cho lần học lại).</summary>
    public const string Reschedule = "reschedule";

    public static readonly string[] All = { Completed, Reschedule };
}
