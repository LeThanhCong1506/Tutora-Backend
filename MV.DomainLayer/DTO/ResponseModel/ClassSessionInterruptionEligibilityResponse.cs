namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// Cho FE biết ngay trong lúc đang học buổi có đủ điều kiện báo ngắt giữa chừng chưa — tránh phải
/// bấm thử "Báo buổi học bị ngắt" mới biết bị từ chối vì chưa đạt ngưỡng % tối thiểu (xem
/// <see cref="MV.ApplicationLayer.Helpers.ClassSessionInterruptionPolicy"/>). CurrentRatio lấy từ
/// dữ liệu Agora thật (thời gian cả 2 bên cùng có mặt / tổng thời lượng dự kiến), không phải đồng
/// hồ tường (elapsed) — chính xác đúng bằng con số RequestInterruptionAsync sẽ dùng để duyệt/từ chối.
/// </summary>
public class ClassSessionInterruptionEligibilityResponse
{
    public bool Eligible { get; set; }
    /// <summary>% đã học thật tính tới lúc gọi (0.0–1.0).</summary>
    public double CurrentRatio { get; set; }
    /// <summary>Ngưỡng % tối thiểu cần đạt — 0.5 cho buổi thường, 0.2 cho buổi đầu tiên của booking.</summary>
    public double RequiredRatio { get; set; }
    /// <summary>False cho buổi phụ (Iscontinuation) và buổi học lại do hoà giải (Isdisputerelearn) —
    /// 2 loại này KHÔNG BAO GIỜ báo ngắt được dù học bao lâu, nên FE nên ẩn hẳn nút thay vì hiện
    /// nút khoá vĩnh viễn. False khác Eligible=false: Eligible có thể đổi thành true khi đạt đủ %,
    /// còn CanEverBeInterrupted=false thì vĩnh viễn không đổi trong suốt buổi.</summary>
    public bool CanEverBeInterrupted { get; set; } = true;
    /// <summary>False nếu đã dạy thật bằng/vượt thời lượng đăng ký của buổi (phần "còn thiếu" đã về
    /// 0) — RequestInterruptionAsync sẽ từ chối báo ngắt dù đã đạt ngưỡng %, vì tạo buổi phụ lúc này
    /// vô nghĩa. Khác CanEverBeInterrupted: cờ này chỉ đúng cho buổi HIỆN TẠI dựa trên thời gian đã
    /// trôi qua (không phải loại buổi), và không tự đổi lại thành true khi thời gian tiếp tục trôi.</summary>
    public bool HasRemainingTime { get; set; } = true;
}
