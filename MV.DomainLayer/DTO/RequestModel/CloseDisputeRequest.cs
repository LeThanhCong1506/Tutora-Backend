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
}

/// <summary>
/// Trạng thái buổi học sau khi admin đóng tranh chấp do hai bên hoà giải.
///
/// Chỉ buổi ở "pending_confirmation" hoặc "completed" mới tạo được tranh chấp (xem
/// <see cref="MV.ApplicationLayer.Helpers.DisputeSettlementPolicy.IsEligibleClassSession"/>), nhưng
/// trạng thái cũ đó không được lưu lại ở đâu — nên admin phải tự chọn đưa buổi về đâu.
/// </summary>
public static class CloseDisputeOutcomes
{
    /// <summary>Buổi học vẫn tính là đã dạy — quyết toán cho gia sư ngay, không hoàn tiền.</summary>
    public const string Completed = "completed";

    /// <summary>Trả buổi về "chờ phụ huynh xác nhận" như trước khi có phản ánh — dùng khi phản ánh
    /// được tạo lúc buổi còn đang chờ xác nhận. Chưa quyết toán: phụ huynh xác nhận (hoặc hết hạn
    /// 12h rồi job tự xác nhận) thì tiền mới chuyển, đúng luồng thường ngày.</summary>
    public const string PendingConfirmation = "pending_confirmation";

    /// <summary>Hai bên thống nhất học lại buổi này — trả buổi về "scheduled", xoá dấu vết điểm danh
    /// của lần trước và KHÔNG quyết toán (tiền vẫn nằm trong booking để dùng cho lần học lại).</summary>
    public const string Reschedule = "reschedule";

    public static readonly string[] All = { Completed, PendingConfirmation, Reschedule };
}
