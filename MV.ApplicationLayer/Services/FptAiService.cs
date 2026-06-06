using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO.ResponseModel;
using System.Net.Http.Headers;
using System.Text.Json;
using MV.DomainLayer.Constants;

namespace MV.ApplicationLayer.Services
{
    public class FptAiService : IFptAiService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _config;
        private readonly ILogger<FptAiService> _logger;
        private readonly string _apiKey;
        private readonly string _baseUrl;

        // Threshold cho validation
        private const double OCR_CONFIDENCE_THRESHOLD = 70.0;

        public FptAiService(HttpClient httpClient, IConfiguration config, ILogger<FptAiService> logger)
        {
            _httpClient = httpClient;
            _config = config;
            _logger = logger;
            _apiKey = config["FptAi:ApiKey"] ?? "";
            _baseUrl = config["FptAi:BaseUrl"] ?? "https://api.fpt.ai/vision/idr/vnm";
        }

        #region Existing Method - ID Card OCR

        public async Task<FptAiIdCardResponse> VerifyIdCardAsync(Stream imageStream, string fileName)
        {
            try
            {
                using var request = new MultipartFormDataContent();
                var fileContent = new StreamContent(imageStream);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue("multipart/form-data");
                request.Add(fileContent, "image", fileName);

                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _baseUrl);
                httpRequest.Headers.Add("api-key", _apiKey);
                httpRequest.Content = request;

                var response = await _httpClient.SendAsync(httpRequest);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("FPT AI ID Card OCR failed with status: {StatusCode}", response.StatusCode);
                    return null!;
                }

                var jsonString = await response.Content.ReadAsStringAsync();
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                return JsonSerializer.Deserialize<FptAiIdCardResponse>(jsonString, options)!;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling FPT AI ID Card OCR");
                return null!;
            }
        }

        #endregion

        #region New Method - Liveness Check

        /// <summary>
        /// Kiểm tra Liveness của video (người thật, cử động thật)
        /// FPT.AI Liveness API endpoint: https://api.fpt.ai/vision/liveness/v3
        /// </summary>
        public async Task<FptAiLivenessResponse> CheckVideoLivenessAsync(Stream videoStream, string fileName)
        {
            try
            {
                var livenessUrl = _config["FptAi:LivenessUrl"] ?? "https://api.fpt.ai/vision/liveness/v3";

                using var request = new MultipartFormDataContent();
                var fileContent = new StreamContent(videoStream);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
                request.Add(fileContent, "file", fileName);

                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, livenessUrl);
                httpRequest.Headers.Add("api-key", _apiKey);
                httpRequest.Content = request;

                var response = await _httpClient.SendAsync(httpRequest);
                var jsonString = await response.Content.ReadAsStringAsync();

                _logger.LogInformation("FPT AI Liveness response: {Response}", jsonString);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("FPT AI Liveness check failed with status: {StatusCode}", response.StatusCode);
                    return new FptAiLivenessResponse
                    {
                        ErrorCode = (int)response.StatusCode,
                        ErrorMessage = "Liveness check failed",
                        Data = null
                    };
                }

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                return JsonSerializer.Deserialize<FptAiLivenessResponse>(jsonString, options) ?? new FptAiLivenessResponse
                {
                    ErrorCode = -1,
                    ErrorMessage = "Failed to parse response"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling FPT AI Liveness API");
                return new FptAiLivenessResponse
                {
                    ErrorCode = -1,
                    ErrorMessage = ex.Message,
                    Data = null
                };
            }
        }

        #endregion

        #region New Method - Text Moderation

        /// <summary>
        /// Kiểm tra nội dung text có an toàn không
        /// Sử dụng local filter + optional external API
        /// </summary>
        public async Task<TextModerationResponse> CheckTextContentSafeAsync(string textContent)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(textContent))
                {
                    return new TextModerationResponse
                    {
                        IsSafe = true,
                        ConfidenceScore = 100,
                        Message = "Empty content is safe"
                    };
                }

                // Normalize text để check
                var normalizedText = NormalizeVietnameseText(textContent.ToLower());
                var violations = new List<ModerationViolation>();

                // Check với danh sách từ cấm tiếng Việt
                var bannedWords = GetVietnameseBannedWords();
                foreach (var word in bannedWords)
                {
                    var index = normalizedText.IndexOf(word, StringComparison.OrdinalIgnoreCase);
                    if (index >= 0)
                    {
                        violations.Add(new ModerationViolation
                        {
                            Category = ModerationConstants.Category.Profanity,
                            Severity = ModerationConstants.Severity.High,
                            MatchedText = MaskWord(word),
                            StartIndex = index,
                            EndIndex = index + word.Length
                        });
                    }
                }

                // Check từ khóa nhạy cảm
                var sensitiveWords = GetSensitiveWords();
                foreach (var word in sensitiveWords)
                {
                    var index = normalizedText.IndexOf(word, StringComparison.OrdinalIgnoreCase);
                    if (index >= 0)
                    {
                        violations.Add(new ModerationViolation
                        {
                            Category = ModerationConstants.Category.SensitiveContent,
                            Severity = ModerationConstants.Severity.Medium,
                            MatchedText = MaskWord(word),
                            StartIndex = index,
                            EndIndex = index + word.Length
                        });
                    }
                }

                // Check thông tin cá nhân nhạy cảm (số điện thoại, email trong bio)
                CheckPersonalInfoLeakage(textContent, violations);

                var isSafe = violations.Count == 0;
                var highSeverityCount = violations.Count(v => v.Severity == ModerationConstants.Severity.High);

                return new TextModerationResponse
                {
                    IsSafe = isSafe,
                    ConfidenceScore = isSafe ? 100 : Math.Max(0, 100 - (violations.Count * 20) - (highSeverityCount * 30)),
                    Violations = violations.Count > 0 ? violations : null,
                    OriginalText = textContent.Length > 100 ? textContent.Substring(0, 100) + "..." : textContent,
                    Message = isSafe ? "Content is safe" : $"Found {violations.Count} violation(s)"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in text moderation");
                // Fail-safe: nếu có lỗi thì cho phép qua nhưng log lại
                return new TextModerationResponse
                {
                    IsSafe = true,
                    ConfidenceScore = 50,
                    Message = "Moderation check failed, content allowed with low confidence"
                };
            }
        }

        private string NormalizeVietnameseText(string text)
        {
            // Normalize các biến thể viết tắt và leet speak phổ biến
            var normalized = text
                .Replace("0", "o")
                .Replace("1", "i")
                .Replace("3", "e")
                .Replace("4", "a")
                .Replace("5", "s")
                .Replace("@", "a")
                .Replace("$", "s");

            return normalized;
        }

        private List<string> GetVietnameseBannedWords()
        {
            // Danh sách từ cấm tiếng Việt (chỉ giữ những từ thật sự xúc phạm)
            return new List<string>
            {
                // Từ tục tiếng Việt phổ biến (đã encode để tránh hiển thị trực tiếp)
                // "dmm", "vcl", "vkl", "dkm", "clm", "cmm",
                // "dit", "lon", "cac", "buoi", "du ma",
                // "ngu", "dien", "khung",
                // Thêm các từ khác theo nhu cầu
            };
        }

        private List<string> GetSensitiveWords()
        {
            return new List<string>
            {
                // Từ khóa nhạy cảm liên quan đến lừa đảo, spam
                "chuyen tien", "so tai khoan", "mat khau",
                "trung thuong", "mien phi 100%", "kiem tien nhanh",
                "dau tu sinh loi", "bitcoin", "crypto",
                // Từ khóa liên quan đến nội dung không phù hợp cho giáo dục
                "casino", "ca cuoc", "sex", "18+"
            };
        }

        private void CheckPersonalInfoLeakage(string text, List<ModerationViolation> violations)
        {
            // Check số điện thoại VN
            var phoneRegex = new System.Text.RegularExpressions.Regex(@"(0|\+84)[0-9]{9,10}");
            var phoneMatches = phoneRegex.Matches(text);
            foreach (System.Text.RegularExpressions.Match match in phoneMatches)
            {
                violations.Add(new ModerationViolation
                {
                    Category = ModerationConstants.Category.PersonalInfo,
                    Severity = ModerationConstants.Severity.Medium,
                    MatchedText = MaskWord(match.Value),
                    StartIndex = match.Index,
                    EndIndex = match.Index + match.Length
                });
            }

            // Check email
            var emailRegex = new System.Text.RegularExpressions.Regex(@"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}");
            var emailMatches = emailRegex.Matches(text);
            foreach (System.Text.RegularExpressions.Match match in emailMatches)
            {
                violations.Add(new ModerationViolation
                {
                    Category = ModerationConstants.Category.PersonalInfo,
                    Severity = ModerationConstants.Severity.Low,
                    MatchedText = MaskWord(match.Value),
                    StartIndex = match.Index,
                    EndIndex = match.Index + match.Length
                });
            }
        }

        private string MaskWord(string word)
        {
            if (word.Length <= 2) return "**";
            return word[0] + new string('*', word.Length - 2) + word[word.Length - 1];
        }

        #endregion
    }
}
