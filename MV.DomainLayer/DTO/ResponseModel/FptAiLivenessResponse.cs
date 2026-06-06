using System.Text.Json.Serialization;

namespace MV.DomainLayer.DTO.ResponseModel
{
    /// <summary>
    /// Response từ FPT.AI Liveness Detection API
    /// Kiểm tra video có phải người thật hay không
    /// </summary>
    public class FptAiLivenessResponse
    {
        [JsonPropertyName("errorCode")]
        public int ErrorCode { get; set; }

        [JsonPropertyName("errorMessage")]
        public string? ErrorMessage { get; set; }

        [JsonPropertyName("data")]
        public LivenessData? Data { get; set; }
    }

    public class LivenessData
    {
        /// <summary>
        /// Kết quả liveness: true = người thật, false = giả mạo
        /// </summary>
        [JsonPropertyName("is_live")]
        public bool IsLive { get; set; }

        /// <summary>
        /// Độ tin cậy của kết quả (0-100)
        /// </summary>
        [JsonPropertyName("liveness_score")]
        public double LivenessScore { get; set; }

        /// <summary>
        /// Loại tấn công phát hiện (nếu có): "printed_photo", "screen_replay", "mask", "none"
        /// </summary>
        [JsonPropertyName("attack_type")]
        public string? AttackType { get; set; }

        /// <summary>
        /// Phát hiện khuôn mặt trong video hay không
        /// </summary>
        [JsonPropertyName("face_detected")]
        public bool FaceDetected { get; set; }

        /// <summary>
        /// Số frame được phân tích
        /// </summary>
        [JsonPropertyName("frames_analyzed")]
        public int FramesAnalyzed { get; set; }

        /// <summary>
        /// Chất lượng video (0-100)
        /// </summary>
        [JsonPropertyName("video_quality")]
        public double VideoQuality { get; set; }
    }
}
