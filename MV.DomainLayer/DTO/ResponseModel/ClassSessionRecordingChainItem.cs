namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// Một buổi trong chuỗi buổi liên kết (bù/phụ/học lại — mọi loại đều tái dùng
/// <c>Originalsessionid</c>), kèm trạng thái ghi hình của riêng buổi đó. Danh sách trả về theo thứ
/// tự thời gian tăng dần, <see cref="Label"/> đánh số lại từ "Buổi 1" bất kể lý do liên kết là gì.
/// </summary>
public class ClassSessionRecordingChainItem
{
    public int ClassSessionId { get; set; }
    public string Label { get; set; } = null!;
    public DateTime ScheduledStart { get; set; }
    /// <summary>True nếu đây là buổi đang được xem trên trang chi tiết gọi API này.</summary>
    public bool IsCurrent { get; set; }
    public string Status { get; set; } = null!;
    public string? StreamUrl { get; set; }
    public bool Available { get; set; }
}
