namespace MV.DomainLayer.DTO.ResponseModel;

public class CccdUploadResponse
{
    public bool OcrSuccess { get; set; }
    /// <summary>
    /// True when verified OCR data changed the canonical personal information on the account.
    /// The client uses this to explain the change to the account owner.
    /// Luồng gia sư KHÔNG tự ghi nữa (xem <see cref="RequiresProfileConfirmation"/>) nên cờ này
    /// chỉ còn bật ở các luồng cố ý auto-fill.
    /// </summary>
    public bool ProfileDataUpdated { get; set; }

    /// <summary>
    /// True khi OCR đọc được dữ liệu khác với hồ sơ hiện tại và ĐANG CHỜ chủ tài khoản xác nhận.
    /// Danh tính đã được xác minh (ảnh + số CCCD đã lưu), chỉ các trường hồ sơ là chưa ghi.
    /// </summary>
    public bool RequiresProfileConfirmation { get; set; }

    /// <summary>Các trường hồ sơ sẽ đổi nếu người dùng xác nhận. Rỗng khi hồ sơ đã khớp CCCD.</summary>
    public List<EkycProfileFieldChange> PendingProfileChanges { get; set; } = new();

    public string? IdentityNumber { get; set; }
    public string? FullName { get; set; }
    public string? DateOfBirth { get; set; }
    public string? Gender { get; set; }
    /// <summary>Quê quán ("home" trên CCCD). Chỉ để hiển thị/đối chiếu — không lưu thành cột riêng.</summary>
    public string? Hometown { get; set; }
    /// <summary>Địa chỉ thường trú ("Nơi thường trú" trên CCCD), khác với khu vực dạy của gia sư.</summary>
    public string? Address { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Kết quả sau khi chủ tài khoản bấm xác nhận áp dụng dữ liệu CCCD vào hồ sơ.
/// </summary>
public class CccdProfileConfirmResponse
{
    /// <summary>Các trường vừa được ghi. Rỗng nghĩa là hồ sơ đã khớp CCCD từ trước (gọi lại không hại gì).</summary>
    public List<EkycProfileFieldChange> AppliedChanges { get; set; } = new();

    public string? FullName { get; set; }
    public string? DateOfBirth { get; set; }
    public string? Gender { get; set; }
    public string? Hometown { get; set; }
    public string? Address { get; set; }
    public string Message { get; set; } = string.Empty;
}
