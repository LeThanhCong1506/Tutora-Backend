namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// Một trường hồ sơ sẽ đổi (hoặc vừa đổi) theo dữ liệu CCCD. Client dựng màn hình
/// "giá trị hiện tại → giá trị trên CCCD" từ danh sách này, không tự suy diễn nhãn.
/// </summary>
public class EkycProfileFieldChange
{
    /// <summary>Khóa trường (fullName | dateOfBirth | gender | address).</summary>
    public string Field { get; set; } = string.Empty;

    /// <summary>Nhãn tiếng Việt để hiển thị.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Giá trị đang lưu trong hồ sơ. Null khi hồ sơ chưa có gì.</summary>
    public string? CurrentValue { get; set; }

    /// <summary>Giá trị đọc được trên CCCD.</summary>
    public string? NewValue { get; set; }
}
