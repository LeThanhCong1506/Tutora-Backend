namespace MV.DomainLayer.Configuration
{
    public class GoogleGeminiSettings
    {
        public const string SectionName = "GoogleGemini";

        public string ApiKey { get; set; } = string.Empty;
        // gemini-pro không hỗ trợ video. gemini-2.5-flash đã bị Google chặn cấp cho API key mới
        // (deprecation chính thức 2026-10-16 nhưng chặn sớm hơn cho key mới) — dùng bản kế nhiệm.
        public string Model { get; set; } = "gemini-3.6-flash";
        /// <summary>Model riêng cho lượt chép lời. Chép lời chỉ là ghi lại đúng những gì nghe được, không
        /// cần suy luận như tóm tắt, nên dùng bản Lite nhanh hơn (~350 so với ~280 token/giây). Đây là
        /// chặng sinh nhiều token nhất (transcript dài gấp 10-15 lần tóm tắt) nên chênh lệch tốc độ ở
        /// đây có giá trị nhất.</summary>
        public string TranscriptModel { get; set; } = "gemini-3.5-flash-lite";
        public int MaxOutputTokens { get; set; } = 4096;
        /// <summary>Giới hạn token riêng cho lượt tóm tắt+transcript (transcript đầy đủ 1 buổi học dài có thể
        /// rất nhiều token) — dùng MaxOutputTokens thường (2048-4096) sẽ bị Gemini cắt giữa chừng, JSON trả về
        /// không đóng được nên parse lỗi (đã gặp thực tế: JsonException "Expected end of string").</summary>
        public int TranscriptMaxOutputTokens { get; set; } = 65536;
        public float Temperature { get; set; } = 0.7f;
    }
}
