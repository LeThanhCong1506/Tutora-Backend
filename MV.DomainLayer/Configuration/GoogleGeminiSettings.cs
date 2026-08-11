namespace MV.DomainLayer.Configuration
{
    public class GoogleGeminiSettings
    {
        public const string SectionName = "GoogleGemini";

        public string ApiKey { get; set; } = string.Empty;
        // gemini-pro không hỗ trợ video. gemini-2.5-flash đã bị Google chặn cấp cho API key mới
        // (deprecation chính thức 2026-10-16 nhưng chặn sớm hơn cho key mới) — dùng bản kế nhiệm.
        public string Model { get; set; } = "gemini-3.6-flash";
        public int MaxOutputTokens { get; set; } = 4096;
        /// <summary>Giới hạn token riêng cho lượt tóm tắt+transcript (transcript đầy đủ 1 buổi học dài có thể
        /// rất nhiều token) — dùng MaxOutputTokens thường (2048-4096) sẽ bị Gemini cắt giữa chừng, JSON trả về
        /// không đóng được nên parse lỗi (đã gặp thực tế: JsonException "Expected end of string").</summary>
        public int TranscriptMaxOutputTokens { get; set; } = 65536;
        public float Temperature { get; set; } = 0.7f;
        /// <summary>MEDIA_RESOLUTION_LOW kéo dài thời lượng video xử lý được (buổi học có thể 2-4 tiếng)
        /// bằng cách giảm token/frame — đổi lại ít chi tiết hình ảnh hơn, chấp nhận được cho tóm tắt nội dung.</summary>
        public string MediaResolution { get; set; } = "MEDIA_RESOLUTION_LOW";
    }
}
