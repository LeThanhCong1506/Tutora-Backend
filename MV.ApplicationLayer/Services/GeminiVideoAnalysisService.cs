using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Configuration;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Exceptions;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MV.ApplicationLayer.Services;

/// <summary>
/// Dùng Gemini File API + generateContent để phân tích video buổi học đã ghi — tóm tắt cho học
/// sinh, sinh nội dung báo cáo có cấu trúc cho gia sư, và trả lời câu hỏi hỏi tiếp dựa trên tóm tắt.
/// Gọi thẳng REST API (không qua SDK) — cùng phong cách với DisputeClassificationService (Groq).
/// </summary>
public class GeminiVideoAnalysisService : IGeminiVideoAnalysisService
{
    // File API >2GB bị Gemini từ chối thẳng — chặn trước khi tốn công tải/upload.
    private const long MaxFileSizeBytes = 2_000_000_000;
    private const int FileActivePollIntervalSeconds = 5;
    private const int FileActiveMaxWaitMinutes = 10;

    private readonly HttpClient _httpClient;
    private readonly GoogleGeminiSettings _settings;
    private readonly ILogger<GeminiVideoAnalysisService> _logger;
    private static readonly JsonSerializerOptions CamelCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public GeminiVideoAnalysisService(
        HttpClient httpClient,
        IOptions<GoogleGeminiSettings> settings,
        ILogger<GeminiVideoAnalysisService> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<GeminiUploadedFile> UploadVideoAsync(
        Stream videoStream, long contentLength, string mimeType, string displayName, CancellationToken ct = default)
    {
        EnsureConfigured();
        if (contentLength > MaxFileSizeBytes)
            throw new GeminiVideoTooLargeException();

        // Bước 1: khởi tạo resumable upload session, lấy URL upload thật từ header X-Goog-Upload-URL.
        using var startRequest = new HttpRequestMessage(HttpMethod.Post, $"/upload/v1beta/files?key={_settings.ApiKey}");
        startRequest.Headers.TryAddWithoutValidation("X-Goog-Upload-Protocol", "resumable");
        startRequest.Headers.TryAddWithoutValidation("X-Goog-Upload-Command", "start");
        startRequest.Headers.TryAddWithoutValidation("X-Goog-Upload-Header-Content-Length", contentLength.ToString());
        startRequest.Headers.TryAddWithoutValidation("X-Goog-Upload-Header-Content-Type", mimeType);
        var startBody = JsonSerializer.Serialize(new { file = new { display_name = displayName } });
        startRequest.Content = new StringContent(startBody, Encoding.UTF8, "application/json");

        using var startResponse = await _httpClient.SendAsync(startRequest, ct);
        if (!startResponse.IsSuccessStatusCode)
        {
            var body = await startResponse.Content.ReadAsStringAsync(ct);
            _logger.LogError("Gemini upload-start lỗi: {StatusCode} - {Body}", startResponse.StatusCode, body);
            throw new GeminiApiException((int)startResponse.StatusCode, "Không thể bắt đầu upload video lên Gemini.");
        }

        if (!startResponse.Headers.TryGetValues("X-Goog-Upload-URL", out var uploadUrls))
            throw new GeminiFileProcessingException("Gemini không trả về địa chỉ upload.");
        var uploadUrl = uploadUrls.First();

        // Bước 2: đẩy bytes thật — stream thẳng từ videoStream (Drive), không đọc hết vào RAM trước.
        using var uploadRequest = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
        uploadRequest.Headers.TryAddWithoutValidation("X-Goog-Upload-Offset", "0");
        uploadRequest.Headers.TryAddWithoutValidation("X-Goog-Upload-Command", "upload, finalize");
        uploadRequest.Content = new StreamContent(videoStream);
        uploadRequest.Content.Headers.ContentLength = contentLength;
        uploadRequest.Content.Headers.ContentType = new MediaTypeHeaderValue(mimeType);

        using var uploadResponse = await _httpClient.SendAsync(uploadRequest, ct);
        var uploadResponseBody = await uploadResponse.Content.ReadAsStringAsync(ct);
        if (!uploadResponse.IsSuccessStatusCode)
        {
            _logger.LogError("Gemini upload video lỗi: {StatusCode} - {Body}", uploadResponse.StatusCode, uploadResponseBody);
            throw new GeminiApiException((int)uploadResponse.StatusCode, "Upload video lên Gemini thất bại.");
        }

        var parsed = JsonSerializer.Deserialize<GeminiFileEnvelope>(uploadResponseBody, CamelCaseOptions);
        var file = parsed?.File;
        if (file?.Name is null || file.Uri is null)
            throw new GeminiResponseParseException("Gemini trả về thông tin file không hợp lệ sau khi upload.");

        _logger.LogInformation("Đã upload video lên Gemini: name={Name} state={State}", file.Name, file.State);
        return new GeminiUploadedFile(file.Name, file.Uri);
    }

    public async Task WaitForFileActiveAsync(string fileName, CancellationToken ct = default)
    {
        EnsureConfigured();
        var deadline = DateTime.UtcNow.AddMinutes(FileActiveMaxWaitMinutes);

        while (true)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/v1beta/{fileName}?key={_settings.ApiKey}");
            using var response = await _httpClient.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Gemini kiểm tra trạng thái file lỗi: {StatusCode} - {Body}", response.StatusCode, body);
                throw new GeminiApiException((int)response.StatusCode, "Không kiểm tra được trạng thái xử lý video trên Gemini.");
            }

            var file = JsonSerializer.Deserialize<GeminiFile>(body, CamelCaseOptions);
            if (string.Equals(file?.State, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                return;
            if (string.Equals(file?.State, "FAILED", StringComparison.OrdinalIgnoreCase))
                throw new GeminiFileProcessingException("Gemini xử lý video thất bại.");

            if (DateTime.UtcNow >= deadline)
                throw new GeminiFileProcessingException("Gemini xử lý video quá lâu, vui lòng thử lại sau.");

            await Task.Delay(TimeSpan.FromSeconds(FileActivePollIntervalSeconds), ct);
        }
    }

    public async Task<GeminiVideoStudentAnalysis> AnalyzeVideoForStudentAsync(string fileUri, string mimeType, CancellationToken ct = default)
    {
        const string prompt = """
            Bạn là trợ lý xử lý video buổi học 1-kèm-1 giữa gia sư và học sinh. Hãy xem kỹ video và trả về đúng
            2 nội dung sau, viết bằng tiếng Việt:
            - summary: Bản tóm tắt dễ hiểu cho học sinh đọc lại, gồm nội dung chính đã học/dạy, các điểm quan
              trọng/công thức/kết luận đáng nhớ, và bài tập về nhà (nếu có). Viết mạch lạc, chia đoạn rõ ràng,
              không chào hỏi mở đầu, không lặp lại nguyên văn lời nói.
            - transcript: Bản chép lời (transcript) đầy đủ, theo sát những gì gia sư và học sinh thực sự nói
              trong video, theo đúng trình tự thời gian. Mỗi câu/đoạn ghi rõ người nói ("Gia sư:" hoặc
              "Học sinh:") ở đầu dòng. Không tóm lược, không bỏ sót đoạn hội thoại quan trọng.
            """;

        var schema = new GeminiSchema
        {
            Type = "OBJECT",
            Properties = new Dictionary<string, GeminiSchema>
            {
                ["summary"] = new() { Type = "STRING" },
                ["transcript"] = new() { Type = "STRING" }
            },
            Required = ["summary", "transcript"]
        };

        var requestBody = BuildGenerateContentRequest(fileUri, mimeType, prompt, schema);
        var text = await SendGenerateContentAsync(requestBody, ct);

        var parsed = JsonSerializer.Deserialize<StudentAnalysisJson>(text, CamelCaseOptions);
        if (parsed?.Summary is null || parsed.Transcript is null)
            throw new GeminiResponseParseException("Gemini trả về tóm tắt/transcript không hợp lệ.");
        return new GeminiVideoStudentAnalysis(parsed.Summary.Trim(), parsed.Transcript.Trim());
    }

    public async Task<GeminiVideoStudentAnalysis> VerifyStudentAnalysisAsync(
        string fileUri, string mimeType, GeminiVideoStudentAnalysis draft, CancellationToken ct = default)
    {
        var prompt = $"""
            Bạn vừa xem video buổi học 1-kèm-1 này và tạo ra bản nháp tóm tắt + transcript dưới đây. Hãy xem lại
            VIDEO một lần nữa thật kỹ, đối chiếu với bản nháp, kiểm tra xem có bỏ sót nội dung quan trọng nào
            không (kiến thức đã dạy, công thức, kết luận, bài tập về nhà, đoạn hội thoại đáng chú ý...) hoặc có
            chi tiết nào ghi sai không.
            - Nếu bản nháp đã đầy đủ và chính xác: giữ nguyên, trả lại đúng như cũ.
            - Nếu thiếu hoặc sai: bổ sung/sửa lại rồi trả về bản đã hoàn thiện.
            Trả về đúng 2 field summary/transcript (bản CUỐI CÙNG, không phải phần nhận xét về bản nháp).

            BẢN NHÁP - summary:
            {draft.Summary}

            BẢN NHÁP - transcript:
            {draft.Transcript}
            """;

        var schema = new GeminiSchema
        {
            Type = "OBJECT",
            Properties = new Dictionary<string, GeminiSchema>
            {
                ["summary"] = new() { Type = "STRING" },
                ["transcript"] = new() { Type = "STRING" }
            },
            Required = ["summary", "transcript"]
        };

        var requestBody = BuildGenerateContentRequest(fileUri, mimeType, prompt, schema);
        var text = await SendGenerateContentAsync(requestBody, ct);

        var parsed = JsonSerializer.Deserialize<StudentAnalysisJson>(text, CamelCaseOptions);
        if (parsed?.Summary is null || parsed.Transcript is null)
            throw new GeminiResponseParseException("Gemini trả về bản kiểm tra lại không hợp lệ.");
        return new GeminiVideoStudentAnalysis(parsed.Summary.Trim(), parsed.Transcript.Trim());
    }

    public async Task<TutorReportAiFillResult> GenerateTutorReportFieldsAsync(string fileUri, string mimeType, CancellationToken ct = default)
    {
        const string prompt = """
            Bạn là trợ lý giúp gia sư viết báo cáo sau buổi học 1-kèm-1, dựa trên video ghi lại buổi học.
            Hãy xem video và trả về đúng 3 nội dung sau, viết bằng tiếng Việt, ở góc nhìn của gia sư viết cho phụ huynh/học sinh đọc:
            - lessonContent: Nội dung đã dạy trong buổi học.
            - homework: Bài tập về nhà đã giao cho học sinh (để trống nếu không có).
            - tutorNotes: Ghi chú thêm của gia sư về buổi học (thái độ học, điểm cần cải thiện...).
            """;

        var schema = new GeminiSchema
        {
            Type = "OBJECT",
            Properties = new Dictionary<string, GeminiSchema>
            {
                ["lessonContent"] = new() { Type = "STRING" },
                ["homework"] = new() { Type = "STRING" },
                ["tutorNotes"] = new() { Type = "STRING" }
            },
            Required = ["lessonContent", "homework", "tutorNotes"]
        };

        var requestBody = BuildGenerateContentRequest(fileUri, mimeType, prompt, schema);
        var text = await SendGenerateContentAsync(requestBody, ct);

        var parsed = JsonSerializer.Deserialize<TutorReportAiFillResult>(text, CamelCaseOptions);
        if (parsed is null)
            throw new GeminiResponseParseException("Gemini trả về nội dung báo cáo không hợp lệ.");
        return parsed;
    }

    public async Task<string> AskFollowUpAsync(
        string summaryText, IReadOnlyList<GeminiChatTurn> history, string question, CancellationToken ct = default)
    {
        EnsureConfigured();

        var contents = new List<object>();
        foreach (var turn in history)
        {
            // Gemini dùng role "model" cho lượt AI, không phải "assistant".
            var role = string.Equals(turn.Role, "assistant", StringComparison.OrdinalIgnoreCase) ? "model" : "user";
            contents.Add(new { role, parts = new object[] { new { text = turn.Content } } });
        }
        contents.Add(new { role = "user", parts = new object[] { new { text = question } } });

        var requestBody = new
        {
            systemInstruction = new
            {
                parts = new object[]
                {
                    new
                    {
                        text = "Bạn là trợ lý trả lời câu hỏi của học sinh về buổi học, dựa trên bản tóm tắt sau đây. " +
                            "Chỉ trả lời trong phạm vi nội dung tóm tắt, nếu câu hỏi ngoài phạm vi thì nói rõ là " +
                            "không có thông tin trong buổi học này. Trả lời ngắn gọn, bằng tiếng Việt.\n\n" +
                            $"TÓM TẮT BUỔI HỌC:\n{summaryText}"
                    }
                }
            },
            contents,
            generationConfig = new
            {
                temperature = _settings.Temperature,
                maxOutputTokens = _settings.MaxOutputTokens
            }
        };

        var text = await SendGenerateContentAsync(requestBody, ct);
        return text.Trim();
    }

    private object BuildGenerateContentRequest(string fileUri, string mimeType, string prompt, GeminiSchema? jsonSchema)
    {
        var generationConfig = jsonSchema is null
            ? new Dictionary<string, object?>
            {
                ["temperature"] = _settings.Temperature,
                ["maxOutputTokens"] = _settings.MaxOutputTokens,
                ["mediaResolution"] = _settings.MediaResolution
            }
            : new Dictionary<string, object?>
            {
                ["temperature"] = _settings.Temperature,
                ["maxOutputTokens"] = _settings.MaxOutputTokens,
                ["mediaResolution"] = _settings.MediaResolution,
                ["responseMimeType"] = "application/json",
                ["responseSchema"] = jsonSchema
            };

        return new
        {
            contents = new object[]
            {
                new
                {
                    role = "user",
                    parts = new object[]
                    {
                        new { fileData = new { mimeType, fileUri } },
                        new { text = prompt }
                    }
                }
            },
            generationConfig
        };
    }

    private async Task<string> SendGenerateContentAsync(object requestBody, CancellationToken ct)
    {
        EnsureConfigured();

        var json = JsonSerializer.Serialize(requestBody, CamelCaseOptions);
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"/v1beta/models/{_settings.Model}:generateContent?key={_settings.ApiKey}")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        using var response = await _httpClient.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Gemini generateContent lỗi: {StatusCode} - {Body}", response.StatusCode, responseBody);
            throw new GeminiApiException((int)response.StatusCode, "Gemini không thể xử lý yêu cầu này.");
        }

        var parsed = JsonSerializer.Deserialize<GeminiGenerateContentResponse>(responseBody, CamelCaseOptions);
        var candidate = parsed?.Candidates?.FirstOrDefault();
        if (candidate is null)
            throw new GeminiResponseParseException("Gemini không trả về kết quả nào.");

        if (string.Equals(candidate.FinishReason, "SAFETY", StringComparison.OrdinalIgnoreCase))
            throw new GeminiResponseParseException("Nội dung video bị chặn bởi bộ lọc an toàn của Gemini, không thể tóm tắt.");

        var text = candidate.Content?.Parts?.FirstOrDefault()?.Text;
        if (string.IsNullOrWhiteSpace(text))
            throw new GeminiResponseParseException("Gemini trả về nội dung rỗng.");

        return text;
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            _logger.LogError("Google Gemini API key is not configured.");
            throw new GeminiApiException(500, "Dịch vụ tóm tắt video chưa được cấu hình.");
        }
    }

    #region Response Models

    private class StudentAnalysisJson
    {
        public string? Summary { get; set; }
        public string? Transcript { get; set; }
    }

    private class GeminiFileEnvelope
    {
        public GeminiFile? File { get; set; }
    }

    private class GeminiFile
    {
        public string? Name { get; set; }
        public string? Uri { get; set; }
        public string? State { get; set; }
    }

    private class GeminiSchema
    {
        public string Type { get; set; } = "STRING";
        public Dictionary<string, GeminiSchema>? Properties { get; set; }
        public string[]? Required { get; set; }
    }

    private class GeminiGenerateContentResponse
    {
        public List<GeminiCandidate>? Candidates { get; set; }
    }

    private class GeminiCandidate
    {
        public GeminiContent? Content { get; set; }
        public string? FinishReason { get; set; }
    }

    private class GeminiContent
    {
        public List<GeminiPart>? Parts { get; set; }
        public string? Role { get; set; }
    }

    private class GeminiPart
    {
        public string? Text { get; set; }
    }

    #endregion
}
