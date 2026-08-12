namespace MV.DomainLayer.Configuration
{
    public class LocalStorageSettings
    {
        public const string SectionName = "LocalStorage";

        /// <summary>Thư mục gốc trên VPS lưu file public (avatar, tài liệu học tập...) — phải nằm ngoài
        /// thư mục source/deploy để không bị xoá mỗi lần deploy lại, và tài khoản chạy app phải có quyền ghi.</summary>
        public string PublicRoot { get; set; } = string.Empty;

        /// <summary>Thư mục gốc lưu file private (CCCD, ảnh proof payout...) — KHÔNG serve qua static
        /// files, chỉ đọc được qua PrivateFileController sau khi xác thực chữ ký + hạn dùng.</summary>
        public string PrivateRoot { get; set; } = string.Empty;

        /// <summary>Base URL public để ghép thành link đầy đủ trả về, vd "https://api.tutora.vn" (không có dấu / cuối).</summary>
        public string PublicBaseUrl { get; set; } = string.Empty;

        /// <summary>Request path mà static files middleware serve PublicRoot ra, vd "/uploads".</summary>
        public string PublicRequestPath { get; set; } = "/uploads";

        /// <summary>Khoá ký HMAC-SHA256 cho signed URL của file private — đặt riêng, không dùng chung Jwt:Key.</summary>
        public string SigningKey { get; set; } = string.Empty;
    }
}
