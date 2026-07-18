namespace MV.DomainLayer.Configuration;

/// <summary>
/// Cấu hình đẩy file record lên Google Drive của một tài khoản cá nhân (Google One).
///
/// Vì Google Drive cá nhân KHÔNG dùng được service account, phải xác thực bằng
/// OAuth 2.0 với refresh token của chính tài khoản đó (vd tutoravn@gmail.com).
/// </summary>
public class GoogleDriveSettings
{
    public const string SectionName = "GoogleDrive";

    /// <summary>Bật/tắt relay S3 → Google Drive. Để false nếu chỉ muốn lưu ở S3.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>OAuth Client ID (Google Cloud Console → Credentials).</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>OAuth Client Secret.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Refresh token lấy 1 lần bằng OAuth cho tài khoản Drive đích.</summary>
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>(Tùy chọn) ID thư mục Drive để chứa video. Trống = My Drive gốc.</summary>
    public string? FolderId { get; set; }
}
