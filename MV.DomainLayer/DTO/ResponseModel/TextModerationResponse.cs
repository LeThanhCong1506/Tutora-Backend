using System.Text.Json.Serialization;

namespace MV.DomainLayer.DTO.ResponseModel
{
    /// <summary>
    /// Response từ Text Moderation API
    /// Kiểm tra nội dung text có an toàn không
    /// </summary>
    public class TextModerationResponse
    {
        /// <summary>
        /// Nội dung có an toàn không
        /// </summary>
        public bool IsSafe { get; set; }

        /// <summary>
        /// Độ tin cậy của kết quả (0-100)
        /// </summary>
        public double ConfidenceScore { get; set; }

        /// <summary>
        /// Danh sách các vi phạm phát hiện được (nếu có)
        /// </summary>
        public List<ModerationViolation>? Violations { get; set; }

        /// <summary>
        /// Nội dung gốc đã kiểm tra
        /// </summary>
        public string? OriginalText { get; set; }

        /// <summary>
        /// Message tổng quan
        /// </summary>
        public string? Message { get; set; }
    }

    public class ModerationViolation
    {
        /// <summary>
        /// Loại vi phạm: "profanity", "hate_speech", "sexual_content", "violence", "spam", "personal_info"
        /// </summary>
        public string? Category { get; set; }

        /// <summary>
        /// Mức độ nghiêm trọng: "low", "medium", "high"
        /// </summary>
        public string? Severity { get; set; }

        /// <summary>
        /// Từ/cụm từ vi phạm (đã được mask một phần)
        /// </summary>
        public string? MatchedText { get; set; }

        /// <summary>
        /// Vị trí bắt đầu trong text gốc
        /// </summary>
        public int StartIndex { get; set; }

        /// <summary>
        /// Vị trí kết thúc trong text gốc
        /// </summary>
        public int EndIndex { get; set; }
    }
}
