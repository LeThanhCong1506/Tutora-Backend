using System.Text.Json.Serialization;

namespace MV.DomainLayer.DTO.ResponseModel
{
    /// <summary>
    /// Response từ FPT.AI Liveness Detection API v3 (POST https://api.fpt.ai/dmp/liveness/v3).
    /// Lưu ý: FPT trả về các giá trị boolean dưới dạng CHUỖI ("true"/"false").
    /// </summary>
    public class FptAiLivenessResponse
    {
        /// <summary>Mã trạng thái xử lý request tổng (chuỗi, "200" = thành công).</summary>
        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("liveness")]
        public LivenessData? Liveness { get; set; }

        [JsonPropertyName("face_match")]
        public FaceMatchData? FaceMatch { get; set; }

        /// <summary>True khi FPT xử lý request thành công (code = "200").</summary>
        [JsonIgnore]
        public bool IsRequestOk => Code == "200";
    }

    public class LivenessData
    {
        /// <summary>Mã kết quả liveness (chuỗi). 200 = OK, các mã khác mô tả lỗi khuôn mặt/video.</summary>
        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        /// <summary>"true" = người thật, "false" = giả mạo.</summary>
        [JsonPropertyName("is_live")]
        public string? IsLiveRaw { get; set; }

        /// <summary>Xác suất giả mạo (chuỗi số, càng thấp càng tốt).</summary>
        [JsonPropertyName("spoof_prob")]
        public string? SpoofProb { get; set; }

        [JsonPropertyName("need_to_review")]
        public string? NeedToReview { get; set; }

        /// <summary>"true" = phát hiện deepfake.</summary>
        [JsonPropertyName("is_deepfake")]
        public string? IsDeepfakeRaw { get; set; }

        [JsonPropertyName("deepfake_prob")]
        public string? DeepfakeProb { get; set; }

        [JsonPropertyName("warning")]
        public string? Warning { get; set; }

        [JsonIgnore]
        public bool IsLive => string.Equals(IsLiveRaw, "true", System.StringComparison.OrdinalIgnoreCase);

        [JsonIgnore]
        public bool IsDeepfake => string.Equals(IsDeepfakeRaw, "true", System.StringComparison.OrdinalIgnoreCase);
    }

    public class FaceMatchData
    {
        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("isMatch")]
        public string? IsMatch { get; set; }

        [JsonPropertyName("similarity")]
        public string? Similarity { get; set; }

        [JsonPropertyName("warning")]
        public string? Warning { get; set; }
    }
}
