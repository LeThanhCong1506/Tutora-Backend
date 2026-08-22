namespace MV.DomainLayer.DTO.ResponseModel;

public class CccdUploadResponse
{
    public bool OcrSuccess { get; set; }
    /// <summary>
    /// True when verified OCR data changed the canonical personal information on the account.
    /// The client uses this to explain the change to the account owner.
    /// </summary>
    public bool ProfileDataUpdated { get; set; }
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
