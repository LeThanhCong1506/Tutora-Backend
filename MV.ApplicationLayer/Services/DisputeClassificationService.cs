using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.ResponseModel;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MV.ApplicationLayer.Services
{
    /// <summary>
    /// Dùng Groq AI (Llama 3.3 70B) để phân loại mức độ ưu tiên xử lý cho tranh chấp,
    /// dựa trên loại tranh chấp và nội dung khiếu nại của người dùng.
    /// </summary>
    public class DisputeClassificationService : IDisputeClassificationService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<DisputeClassificationService> _logger;
        private readonly string _groqApiKey;
        private const string GROQ_API_URL = "https://api.groq.com/openai/v1/chat/completions";

        public DisputeClassificationService(
            HttpClient httpClient,
            IConfiguration config,
            ILogger<DisputeClassificationService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _groqApiKey = config["Groq:ApiKey"] ?? "";
        }

        public async Task<DisputeClassificationResult?> ClassifyAsync(string disputeType, string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                return null;

            if (string.IsNullOrEmpty(_groqApiKey))
            {
                _logger.LogWarning("Groq API key not configured, skipping dispute classification");
                return null;
            }

            try
            {
                var prompt = $$"""
                    Bạn là hệ thống phân loại mức độ ưu tiên xử lý tranh chấp cho nền tảng dạy kèm trực tuyến.

                    Loại tranh chấp: {{disputeType}} (no_show = gia sư vắng mặt, quality = chất lượng buổi học, payment = vấn đề thanh toán, other = khác)
                    Nội dung khiếu nại: "{{reason}}"

                    TIÊU CHÍ PHÂN LOẠI:
                    - high: an toàn/lạm dụng/quấy rối, gian lận, thiệt hại tài chính lớn, vi phạm nghiêm trọng hoặc lặp lại nhiều lần
                    - medium: gia sư vắng mặt không báo trước, chất lượng giảng dạy kém rõ ràng, ảnh hưởng đáng kể đến trải nghiệm học
                    - low: hiểu lầm nhỏ, sự cố kỹ thuật đơn giản, vấn đề có thể tự giải quyết giữa hai bên

                    Trả về CHỈ JSON, không thêm giải thích:
                    {"priority":"low|medium|high","reason":"giải thích ngắn gọn bằng tiếng Việt, tối đa 1 câu"}
                    """;

                var requestBody = new GroqRequest
                {
                    // llama-3.3-70b-versatile is deprecated on Groq (decommissioned 2026-08-16) — use its
                    // documented replacement. See https://console.groq.com/docs/deprecations.
                    Model = "openai/gpt-oss-120b",
                    Messages =
                    [
                        new GroqMessage { Role = AiChatRole.System, Content = "Bạn phân loại mức độ ưu tiên tranh chấp. Chỉ trả về JSON hợp lệ, không thêm văn bản khác." },
                        new GroqMessage { Role = AiChatRole.User, Content = prompt }
                    ],
                    Temperature = 0.0,
                    MaxTokens = 200,
                    ResponseFormat = new GroqResponseFormat()
                };

                var jsonRequest = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                });

                using var request = new HttpRequestMessage(HttpMethod.Post, GROQ_API_URL);
                request.Headers.Authorization = new AuthenticationHeaderValue(HttpConstants.BearerScheme, _groqApiKey);
                request.Content = new StringContent(jsonRequest, Encoding.UTF8, HttpConstants.JsonContentType);

                var response = await _httpClient.SendAsync(request);
                var responseString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Groq API error during dispute classification: {StatusCode} - {Response}", response.StatusCode, responseString);
                    return null;
                }

                var groqResponse = JsonSerializer.Deserialize<GroqResponse>(responseString,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                var jsonResponse = CleanJsonResponse(groqResponse?.Choices?.FirstOrDefault()?.Message?.Content?.Trim() ?? "");

                if (string.IsNullOrEmpty(jsonResponse))
                    return null;

                var parsed = JsonSerializer.Deserialize<ClassificationAiResponse>(jsonResponse,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                var priority = parsed?.Priority?.Trim().ToLowerInvariant();
                if (priority == null || !DisputePriority.All.Contains(priority))
                {
                    _logger.LogWarning("Groq returned an invalid priority value for dispute classification: '{Priority}'", parsed?.Priority);
                    return null;
                }

                return new DisputeClassificationResult
                {
                    Priority = priority,
                    Reason = parsed?.Reason?.Trim()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling Groq API for dispute classification");
                return null;
            }
        }

        private static string CleanJsonResponse(string response)
        {
            if (string.IsNullOrEmpty(response))
                return response;

            response = Regex.Replace(response, @"```json\s*", "", RegexOptions.IgnoreCase);
            response = Regex.Replace(response, @"```\s*", "");
            response = Regex.Replace(response, @"\s*```", "");

            var startIndex = response.IndexOf('{');
            var endIndex = response.LastIndexOf('}');

            if (startIndex >= 0 && endIndex > startIndex)
            {
                response = response.Substring(startIndex, endIndex - startIndex + 1);
            }

            return response.Trim();
        }

        #region Response Models

        private class GroqRequest
        {
            public string Model { get; set; } = "";
            public List<GroqMessage> Messages { get; set; } = [];
            public double Temperature { get; set; }
            [JsonPropertyName("max_tokens")]
            public int MaxTokens { get; set; }
            [JsonPropertyName("response_format")]
            public GroqResponseFormat? ResponseFormat { get; set; }
        }

        private class GroqResponseFormat
        {
            public string Type { get; set; } = "json_object";
        }

        private class GroqMessage
        {
            public string Role { get; set; } = "";
            public string Content { get; set; } = "";
        }

        private class GroqResponse
        {
            public List<GroqChoice>? Choices { get; set; }
        }

        private class GroqChoice
        {
            public GroqMessage? Message { get; set; }
        }

        private class ClassificationAiResponse
        {
            public string? Priority { get; set; }
            public string? Reason { get; set; }
        }

        #endregion
    }
}
