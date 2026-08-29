namespace MV.DomainLayer.Configuration;

/// <summary>
/// Cấu hình Agora Cloud Recording.
///
/// LƯU Ý:
///   - CustomerId / CustomerSecret là RESTful API credentials
///     (Agora Console → Developer Toolkit → RESTful API → Add a secret),
///     KHÁC với AppCertificate trong <see cref="AgoraSettings"/> dùng để ký RTC token.
///   - Storage* là nơi Agora đẩy file record. Danh sách vendor + region code:
///     https://docs.agora.io/en/cloud-recording/reference/region-vendor
/// </summary>
public class AgoraRecordingSettings
{
    public const string SectionName = "AgoraRecording";

    /// <summary>Bật/tắt tự động record khi tutor check-in. Để false khi chưa cấu hình xong.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>RESTful API Customer ID (dùng cho Basic Auth khi gọi REST API).</summary>
    public string CustomerId { get; set; } = string.Empty;

    /// <summary>RESTful API Customer Secret.</summary>
    public string CustomerSecret { get; set; } = string.Empty;

    /// <summary>UID (số) của "recorder" video (mix) — phải khác UID của mọi người dùng thật trong kênh.</summary>
    public uint RecorderUid { get; set; } = 999999;

    /// <summary>UID của recorder audio-only — chạy song song với recorder video ở trên trong CÙNG 1
    /// channel, nên bắt buộc phải là 1 UID KHÁC (Agora không cho 2 recorder chung UID trong 1 channel).
    /// Bật/tắt riêng qua <see cref="AudioRecordingEnabled"/> — recorder video vẫn luôn chạy độc lập.</summary>
    public uint AudioRecorderUid { get; set; } = 999998;

    /// <summary>Bật/tắt recorder audio-only song song (mục đích: pipeline AI dùng file audio nhỏ này thay
    /// vì phải tải nguyên video rồi ffmpeg tách audio). Tắt mặc định — bật riêng sau khi đã xác nhận cấu
    /// hình acquire/start audio-only hoạt động đúng trên tài khoản Agora thật, tránh phát sinh phí ghi
    /// hình kép (2 recorder cùng lúc) khi chưa chắc chạy đúng.</summary>
    public bool AudioRecordingEnabled { get; set; } = false;

    /// <summary>Thời hạn token recorder (giây). Phải đủ dài cho buổi học dài. Tối đa 86400 (24h).</summary>
    public uint TokenExpireSeconds { get; set; } = 86400;

    // ── storageConfig của Agora ──────────────────────────────────────────────

    /// <summary>Vendor: 1 = AWS S3, 11 = S3-compatible (Cloudflare R2), 2 = Alibaba, 6 = Azure, 7 = Google Cloud...</summary>
    public int StorageVendor { get; set; } = 1;

    /// <summary>Region code theo vendor (xem doc region-vendor của Agora).</summary>
    public int StorageRegion { get; set; } = 0;

    public string StorageBucket { get; set; } = string.Empty;
    public string StorageAccessKey { get; set; } = string.Empty;
    public string StorageSecretKey { get; set; } = string.Empty;

    /// <summary>
    /// Tên region AWS dạng chuỗi (vd "ap-southeast-1") — dùng cho BE tải/xóa file S3 khi relay lên Drive.
    /// KHÁC <see cref="StorageRegion"/> (mã SỐ mà Agora yêu cầu).
    /// </summary>
    public string StorageRegionName { get; set; } = "ap-southeast-1";

    /// <summary>(Chỉ S3-compatible như R2) endpoint tùy chọn. Để trống với AWS S3.</summary>
    public string? StorageEndpoint { get; set; }

    /// <summary>
    /// Base URL công khai để ghép link xem lại
    /// (vd https://tutora-recordings.s3.ap-southeast-1.amazonaws.com). Để trống nếu chỉ lưu tên file.
    /// </summary>
    public string? PublicUrlBase { get; set; }

    // ── Chất lượng video (mix mode) — mặc định 720p ──────────────────────────
    public int VideoWidth { get; set; } = 1280;
    public int VideoHeight { get; set; } = 720;
    public int VideoFps { get; set; } = 15;
    public int VideoBitrate { get; set; } = 1500;

    /// <summary>
    /// Số giây channel trống (không có luồng audio/video nào) trước khi Agora tự dừng recorder
    /// (maxIdleTime của Cloud Recording). Khoảng hợp lệ theo Agora: [5, 2592000].
    /// </summary>
    public int MaxIdleTimeSeconds { get; set; } = 300;
}
