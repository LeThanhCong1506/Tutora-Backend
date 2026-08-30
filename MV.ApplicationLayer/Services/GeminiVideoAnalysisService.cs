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

    public async Task<string> SummarizeVideoForStudentAsync(string fileUri, string mimeType, CancellationToken ct = default)
    {
        const string prompt = """
            Bạn là trợ lý xử lý bản ghi âm buổi học 1-kèm-1 giữa gia sư và học sinh. Hãy nghe kỹ rồi viết bản
            tóm tắt bằng tiếng Việt, giọng văn gần gũi như đang giải thích lại cho học sinh chứ không phải
            liệt kê khô khan. Dùng markdown (tiêu đề phụ "##", in đậm "**...**" cho từ khoá quan trọng, gạch
            đầu dòng "-" cho danh sách). Công thức/ký hiệu toán học viết bằng LaTeX: đặt giữa 1 cặp dấu $ cho
            công thức ngắn nằm trong câu (vd $x^2 + 1$), giữa 1 cặp dấu $$ cho công thức dài/quan trọng cần
            tách dòng riêng. Không chào hỏi mở đầu, không lặp lại nguyên văn lời nói.

            Các mục có thể đưa vào: nội dung chính đã học/dạy (giải thích ngắn gọn ý nghĩa, không chỉ liệt kê
            tên chủ đề), các điểm quan trọng/công thức/kết luận đáng nhớ, và bài tập về nhà.

            QUAN TRỌNG: chỉ viết những mục thật sự có nội dung trong buổi học. Mục nào không có thì BỎ HẲN,
            không in tiêu đề của mục đó ra. Tuyệt đối không viết những câu như "Không có.", "Không đề cập.",
            "Buổi học này không giao bài tập." — thà thiếu mục còn hơn có mục rỗng.
            """;

        var schema = new GeminiSchema
        {
            Type = "OBJECT",
            Properties = new Dictionary<string, GeminiSchema> { ["summary"] = new() { Type = "STRING" } },
            Required = ["summary"]
        };

        var requestBody = BuildGenerateContentRequest(fileUri, mimeType, prompt, schema);
        var text = await SendGenerateContentAsync(requestBody, _settings.Model, ct);

        var parsed = ParseOrThrow<SummaryJson>(text, "tóm tắt");
        if (string.IsNullOrWhiteSpace(parsed.Summary))
            throw new GeminiResponseParseException("Gemini trả về tóm tắt không hợp lệ.");
        return parsed.Summary.Trim();
    }

    public async Task<string> TranscribeVideoAsync(string fileUri, string mimeType, CancellationToken ct = default)
    {
        const string prompt = """
            Bạn là trợ lý chép lời bản ghi âm buổi học 1-kèm-1 giữa gia sư và học sinh. Hãy nghe kỹ và chép
            lại bằng tiếng Việt toàn bộ hội thoại, theo sát những gì từng người thực sự nói, đúng trình tự
            thời gian. Không tóm lược, không bỏ sót đoạn nào.

            QUY TẮC ĐỊNH DẠNG — bắt buộc tuân thủ tuyệt đối, không có ngoại lệ:
            - MỌI đoạn văn đều phải mở đầu bằng nhãn người nói: "**Gia sư:** " hoặc "**Học sinh:** ".
              Không được để bất kỳ đoạn nào thiếu nhãn.
            - Khi CÙNG một người nói liên tiếp nhiều đoạn, từng đoạn vẫn phải lặp lại nhãn của người đó.
              Không được chỉ ghi nhãn ở đoạn đầu rồi bỏ trống các đoạn sau.
            - Mỗi đoạn cách nhau bằng 1 dòng trống. Không gộp lời của 2 người vào chung 1 đoạn.
            - Không xác định được ai đang nói thì ghi "**Không rõ:** ".
            - Nếu cả buổi chỉ có 1 người nói (ví dụ gia sư thử mic, học sinh chưa vào), vẫn phải gắn nhãn
              cho từng đoạn đúng như trên.
            """;

        var schema = new GeminiSchema
        {
            Type = "OBJECT",
            Properties = new Dictionary<string, GeminiSchema> { ["transcript"] = new() { Type = "STRING" } },
            Required = ["transcript"]
        };

        var requestBody = BuildGenerateContentRequest(fileUri, mimeType, prompt, schema, _settings.TranscriptMaxOutputTokens);
        var text = await SendGenerateContentAsync(requestBody, _settings.TranscriptModel, ct);

        var parsed = ParseOrThrow<TranscriptJson>(text, "hội thoại");
        if (string.IsNullOrWhiteSpace(parsed.Transcript))
            throw new GeminiResponseParseException("Gemini trả về hội thoại không hợp lệ.");
        return parsed.Transcript.Trim();
    }

    /// <summary>Nếu Gemini bị cắt giữa chừng do vượt maxOutputTokens (buổi học quá dài), JSON trả về sẽ dở
    /// dang — ném lỗi có nghĩa cho người dùng thay vì để JsonException thô lộ ra ngoài.</summary>
    private T ParseOrThrow<T>(string text, string label) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(text, CamelCaseOptions)
                ?? throw new GeminiResponseParseException($"Gemini trả về {label} không hợp lệ.");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Gemini trả về JSON {Label} dở dang (khả năng bị cắt do vượt maxOutputTokens).", label);
            throw new GeminiResponseParseException(
                $"Buổi học quá dài, Gemini không viết kịp hết {label} trong giới hạn cho phép. Vui lòng thử lại.");
        }
    }

    public async Task<TutorReportAiFillResult> GenerateTutorReportFieldsAsync(string fileUri, string mimeType, CancellationToken ct = default)
    {
        const string prompt = """
            Bạn là trợ lý giúp gia sư viết báo cáo sau buổi học 1-kèm-1, dựa trên bản ghi âm buổi học.
            Hãy nghe kỹ và trả về đúng 3 nội dung sau, viết bằng tiếng Việt, ở góc nhìn của gia sư viết cho phụ huynh/học sinh đọc:
            - lessonContent: Nội dung đã dạy trong buổi học.
            - homework: Bài tập về nhà đã giao cho học sinh. Nếu buổi học KHÔNG giao bài tập nào, PHẢI ghi
              rõ "Không giao bài tập gì cả." — tuyệt đối không để trống hay chỉ viết vài chữ ngắn.
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
        var text = await SendGenerateContentAsync(requestBody, _settings.Model, ct);

        var parsed = JsonSerializer.Deserialize<TutorReportAiFillResult>(text, CamelCaseOptions);
        if (parsed is null)
            throw new GeminiResponseParseException("Gemini trả về nội dung báo cáo không hợp lệ.");

        // Buổi học không giao bài thì Gemini có thể vẫn trả về rỗng hoặc vài chữ ngắn ("Không có") dù
        // prompt đã dặn — model có thể bỏ qua hướng dẫn, code thì không. Form báo cáo phía FE
        // (LessonReportForm.tsx) yêu cầu tối thiểu 10 ký tự cho field này nếu không để trống hẳn — một
        // câu trả lời kiểu "Không có." (9 ký tự) sẽ bị FE từ chối, buộc gia sư phải tự gõ lại. Chuẩn hoá
        // luôn cả trường hợp "có nội dung nhưng quá ngắn", không chỉ trường hợp rỗng hoàn toàn.
        const int minHomeworkLength = 10;
        if (parsed.Homework == null || parsed.Homework.Trim().Length < minHomeworkLength)
            parsed.Homework = "Không giao bài tập gì cả.";

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
                        text = "Bạn là trợ lý trả lời câu hỏi của học sinh về buổi học đã diễn ra, dựa trên nội dung " +
                            "buổi học dưới đây. Trả lời bằng tiếng Việt, giọng thân thiện như một gia sư đang giải " +
                            "thích lại — đừng chỉ nêu đáp án khô khan, hãy giải thích ngắn gọn tại sao/như thế nào " +
                            "khi câu hỏi cần điều đó. Chỉ trả lời trong phạm vi nội dung buổi học, nếu câu hỏi ngoài " +
                            "phạm vi thì nói rõ là không có thông tin trong buổi học này (không bịa). Được dùng " +
                            "markdown (in đậm, gạch đầu dòng) khi giúp câu trả lời dễ đọc hơn. Công thức/ký hiệu " +
                            "toán học viết bằng LaTeX (đặt giữa 1 cặp dấu $ cho công thức ngắn trong câu, giữa 1 " +
                            "cặp dấu $$ cho công thức dài cần tách dòng riêng). Kết thúc câu trả lời " +
                            "bằng 1 câu ngắn gợi ý học sinh có thể hỏi thêm gì liên quan (nếu còn nội dung đáng hỏi " +
                            "trong buổi học), không cần gợi ý nếu câu hỏi đã bao quát hết.\n\n" +
                            $"NỘI DUNG BUỔI HỌC:\n{summaryText}"
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

        var text = await SendGenerateContentAsync(requestBody, _settings.Model, ct);
        return text.Trim();
    }

    public async Task<string> SynthesizeChainSummaryAsync(
        IReadOnlyList<(string Label, string Summary)> legSummaries, CancellationToken ct = default)
    {
        EnsureConfigured();

        const string instruction = """
            Bạn là trợ lý tổng hợp tóm tắt buổi học 1-kèm-1. Buổi học dưới đây đã bị ngắt giữa chừng
            ít nhất 1 lần nên được chia thành nhiều buổi liên tiếp (buổi bù/buổi phụ/buổi học lại),
            mỗi buổi đã có tóm tắt riêng theo đúng thứ tự thời gian. Hãy đọc và viết lại thành DUY
            NHẤT một bản tóm tắt liền mạch cho toàn bộ nội dung đã học, như thể đó là một buổi học
            liên tục — không nhắc tới việc buổi học bị chia/nối/ngắt, không lặp lại nội dung trùng
            giữa các buổi. Dùng markdown giống các tóm tắt gốc (tiêu đề phụ "##", in đậm "**...**"
            cho từ khoá quan trọng, gạch đầu dòng "-" cho danh sách). Giữ nguyên công thức toán học
            ở dạng LaTeX (giữa cặp dấu $ hoặc $$) nếu các tóm tắt gốc đã viết như vậy.

            QUAN TRỌNG: chỉ viết những mục thật sự có nội dung. Mục nào không có ở bất kỳ buổi nào
            thì BỎ HẲN, không in tiêu đề của mục đó ra.
            """;

        var joined = string.Join(
            "\n\n",
            legSummaries.Select(leg => $"--- {leg.Label} ---\n{leg.Summary}"));
        var prompt = $"{instruction}\n\n{joined}";

        var schema = new GeminiSchema
        {
            Type = "OBJECT",
            Properties = new Dictionary<string, GeminiSchema> { ["summary"] = new() { Type = "STRING" } },
            Required = ["summary"]
        };

        var requestBody = new
        {
            contents = new object[]
            {
                new { role = "user", parts = new object[] { new { text = prompt } } }
            },
            generationConfig = new Dictionary<string, object?>
            {
                ["temperature"] = _settings.Temperature,
                ["maxOutputTokens"] = _settings.MaxOutputTokens,
                ["thinkingConfig"] = MinimalThinkingConfig,
                ["responseMimeType"] = "application/json",
                ["responseSchema"] = schema
            }
        };

        var text = await SendGenerateContentAsync(requestBody, _settings.Model, ct);
        var parsed = ParseOrThrow<SummaryJson>(text, "tóm tắt tổng hợp");
        if (string.IsNullOrWhiteSpace(parsed.Summary))
            throw new GeminiResponseParseException("Gemini trả về tóm tắt tổng hợp không hợp lệ.");
        return parsed.Summary.Trim();
    }

    // Cả 3 tác vụ dùng chung builder này (tóm tắt học sinh, soát lại, auto-fill báo cáo gia sư) đều
    // chỉ "đọc và tường thuật lại" video, không cần suy luận sâu — hạ thinking xuống mức thấp nhất để
    // trả lời nhanh hơn mức mặc định. AskFollowUpAsync (chat hỏi tiếp) KHÔNG dùng builder này, cố tình
    // giữ nguyên thinking mặc định vì trả lời câu hỏi tự do cần suy luận thật.
    //
    // Gemini 3.x đổi hẳn cách cấu hình thinking so với 2.5: không còn "thinkingBudget" (số, 0 = tắt
    // hẳn) mà dùng "thinkingLevel" (chuỗi enum minimal/low/medium/high) — thinkingBudget vẫn được
    // chấp nhận "để tương thích ngược" nhưng Google cảnh báo có thể gây hành vi không như mong đợi
    // trên model 3.x, và dòng Flash 3.x không hỗ trợ tắt hẳn thinking (không có mức "off", thấp nhất
    // là "minimal"). Xem https://ai.google.dev/gemini-api/docs/thinking.
    private static readonly Dictionary<string, object?> MinimalThinkingConfig = new() { ["thinkingLevel"] = "minimal" };

    // Chỉ gửi audio (đã tách khỏi video trước khi upload — xem ClassSessionVideoAiService), không
    // còn frame hình ảnh nào để cấu hình mediaResolution/fps nữa — cắt phần lớn token so với gửi
    // nguyên video, nhanh hơn rõ rệt, đổi lại mất mọi nội dung chỉ hiện trên màn hình mà không nói ra.
    private object BuildGenerateContentRequest(
        string fileUri, string mimeType, string prompt, GeminiSchema? jsonSchema, int? maxOutputTokens = null)
    {
        var tokenLimit = maxOutputTokens ?? _settings.MaxOutputTokens;
        var generationConfig = jsonSchema is null
            ? new Dictionary<string, object?>
            {
                ["temperature"] = _settings.Temperature,
                ["maxOutputTokens"] = tokenLimit,
                ["thinkingConfig"] = MinimalThinkingConfig
            }
            : new Dictionary<string, object?>
            {
                ["temperature"] = _settings.Temperature,
                ["maxOutputTokens"] = tokenLimit,
                ["thinkingConfig"] = MinimalThinkingConfig,
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

    private async Task<string> SendGenerateContentAsync(object requestBody, string model, CancellationToken ct)
    {
        EnsureConfigured();

        var json = JsonSerializer.Serialize(requestBody, CamelCaseOptions);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var responseBody = await PostGenerateContentWithRetryAsync(json, model, ct);
        sw.Stop();

        var parsed = JsonSerializer.Deserialize<GeminiGenerateContentResponse>(responseBody, CamelCaseOptions);
        var candidate = parsed?.Candidates?.FirstOrDefault();
        if (candidate is null)
            throw new GeminiResponseParseException("Gemini không trả về kết quả nào.");

        if (string.Equals(candidate.FinishReason, "SAFETY", StringComparison.OrdinalIgnoreCase))
            throw new GeminiResponseParseException("Nội dung video bị chặn bởi bộ lọc an toàn của Gemini, không thể tóm tắt.");

        var text = candidate.Content?.Parts?.FirstOrDefault()?.Text;
        if (string.IsNullOrWhiteSpace(text))
            throw new GeminiResponseParseException("Gemini trả về nội dung rỗng.");

        // Số liệu thật để xác nhận thinking có đang chiếm phần lớn thời gian không, thay vì đoán —
        // xem lại log này nếu sau khi tắt thinkingConfig vẫn còn chậm (model mới, chưa chắc field
        // thinkingBudget=0 có tác dụng như kỳ vọng).
        if (parsed?.UsageMetadata is { } usage)
        {
            _logger.LogInformation(
                "Gemini usage ({Model}): thoughtsTokens={ThoughtsTokens}, outputTokens={OutputTokens}, totalTokens={TotalTokens}, elapsed={ElapsedMs}ms",
                model, usage.ThoughtsTokenCount, usage.CandidatesTokenCount, usage.TotalTokenCount, sw.ElapsedMilliseconds);
        }

        return text;
    }

    /// <summary>Retry cho lỗi tạm thời (mạng chập chờn, Gemini quá tải/5xx) — video đã tốn công
    /// upload + chờ xử lý xong mới tới bước này, để cả job "chết" vì 1 lần trục trặc thoáng qua thì
    /// người dùng phải tóm tắt lại từ đầu, đắt hơn nhiều so với thử lại ngay tại đây. Không retry lỗi
    /// 4xx (request sai, bị chặn an toàn...) vì thử lại cũng vô ích.
    /// 5 lần / backoff 3s-6s-12s-24s (tổng ~45s chờ giữa các lần) — tăng từ 3 lần/~9s sau khi log
    /// production cho thấy có đợt Gemini 503 (quá tải) kéo dài hơn tổng thời gian của 3 lần thử.</summary>
    private async Task<string> PostGenerateContentWithRetryAsync(string json, string model, CancellationToken ct)
    {
        const int maxAttempts = 5;
        var delay = TimeSpan.FromSeconds(3);

        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post, $"/v1beta/models/{model}:generateContent?key={_settings.ApiKey}")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request, ct);
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts)
            {
                _logger.LogWarning(ex, "Gemini generateContent lỗi mạng, thử lại lần {Attempt}/{Max}.", attempt, maxAttempts);
                await Task.Delay(delay, ct);
                delay += delay;
                continue;
            }

            using (response)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                if (response.IsSuccessStatusCode)
                    return body;

                var isTransient = (int)response.StatusCode >= 500 || (int)response.StatusCode == 429;
                if (isTransient && attempt < maxAttempts)
                {
                    _logger.LogWarning("Gemini generateContent lỗi tạm thời {StatusCode}, thử lại lần {Attempt}/{Max}.",
                        response.StatusCode, attempt, maxAttempts);
                    await Task.Delay(delay, ct);
                    delay += delay;
                    continue;
                }

                _logger.LogError("Gemini generateContent lỗi: {StatusCode} - {Body}", response.StatusCode, body);
                throw new GeminiApiException((int)response.StatusCode, "Gemini không thể xử lý yêu cầu này.");
            }
        }
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

    private class SummaryJson
    {
        public string? Summary { get; set; }
    }

    private class TranscriptJson
    {
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
        public GeminiUsageMetadata? UsageMetadata { get; set; }
    }

    private class GeminiUsageMetadata
    {
        public int? ThoughtsTokenCount { get; set; }
        public int? CandidatesTokenCount { get; set; }
        public int? TotalTokenCount { get; set; }
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
