using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.Settings;

namespace MV.InfrastructureLayer.ExternalServices;

/// <summary>
/// HTTP client that calls the FastAPI Tutor AI ranking service.
/// Implements graceful degradation: any failure returns null so the caller
/// falls back to original SQL-filtered order.
/// </summary>
public class TutorAiClient : ITutorAiClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TutorAiClient> _logger;
    private readonly string? _apiKey;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public TutorAiClient(
        IHttpClientFactory httpClientFactory,
        ILogger<TutorAiClient> logger,
        IOptions<TutorAiSettings> aiSettings)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _apiKey = aiSettings.Value.ApiKey;
    }

    public async Task<List<AiRankedTutor>?> RankAsync(
        string? query,
        IReadOnlyList<string> candidateIds,
        int topK,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(ServiceKeys.HttpClients.TutorAi);

            var requestBody = new AiRankRequest
            {
                Query = query,
                CandidateIds = candidateIds.ToList(),
                TopK = topK
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tutors/recommend")
            {
                Content = JsonContent.Create(requestBody)
            };
            if (!string.IsNullOrWhiteSpace(_apiKey))
                request.Headers.Add("X-API-Key", _apiKey);

            using var response = await client.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "TutorAI rank trả status {StatusCode}. Rơi về thứ tự SQL, query bị bỏ.",
                    (int)response.StatusCode);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<AiRankResponse>(content, _jsonOptions);

            if (result?.Results == null)
            {
                _logger.LogWarning("TutorAI service returned null results. Falling back to SQL order.");
                return null;
            }

            return result.Results
                .Select(r => new AiRankedTutor(r.TutorId, r.Similarity))
                .ToList();
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "TutorAI service timed out. Falling back to SQL order.");
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "TutorAI service HTTP error. Falling back to SQL order.");
            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "TutorAI service returned invalid JSON. Falling back to SQL order.");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error calling TutorAI service. Falling back to SQL order.");
            return null;
        }
    }

    public async Task<float[]?> EmbedAsync(string id, string text, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(ServiceKeys.HttpClients.TutorAi);

            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/embed")
            {
                Content = JsonContent.Create(new EmbedRequest
                {
                    Items = new List<EmbedItem> { new() { Id = id, Text = text } }
                })
            };
            if (!string.IsNullOrWhiteSpace(_apiKey))
                request.Headers.Add("X-API-Key", _apiKey);

            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "TutorAI embed trả về status {StatusCode}. Câu hỏi sẽ được embed lại sau.",
                    (int)response.StatusCode);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<EmbedResponse>(content, _jsonOptions);
            var item = result?.Results?.FirstOrDefault(r => r.Id == id);

            if (item?.Embedding == null || item.Embedding.Count == 0)
            {
                _logger.LogWarning("TutorAI embed không trả vector cho id {Id}: {Error}", id, item?.Error);
                return null;
            }
            return item.Embedding.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lỗi gọi TutorAI embed. Câu hỏi sẽ được embed lại sau.");
            return null;
        }
    }

    public async Task EmbedTutorAsync(string tutorId, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(ServiceKeys.HttpClients.TutorAi);

            using var request = new HttpRequestMessage(
                HttpMethod.Post, $"/api/v1/tutors/{Uri.EscapeDataString(tutorId)}/embed");
            if (!string.IsNullOrWhiteSpace(_apiKey))
                request.Headers.Add("X-API-Key", _apiKey);

            // Embed nhờ Gemini -> có thể vài giây; cho timeout riêng để không kẹt luồng gọi.
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(60));

            using var response = await client.SendAsync(request, cts.Token);
            if (!response.IsSuccessStatusCode)
                _logger.LogWarning(
                    "TutorAI embed gia sư {TutorId} trả status {StatusCode} — sẽ được embed lại ở lần cập nhật sau.",
                    tutorId, (int)response.StatusCode);
        }
        catch (Exception ex)
        {
            // Nuốt lỗi: embed hỏng không được chặn luồng lưu hồ sơ gia sư.
            _logger.LogWarning(ex, "Lỗi gọi TutorAI embed gia sư {TutorId}.", tutorId);
        }
    }

    public async Task<List<AiExtractedQuestion>?> ExtractPdfAsync(
        byte[] pdfBytes, string fileName, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(ServiceKeys.HttpClients.TutorAi);

            using var form = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(pdfBytes);
            fileContent.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
            form.Add(fileContent, "file", fileName);

            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/extract-pdf")
            {
                Content = form
            };
            if (!string.IsNullOrWhiteSpace(_apiKey))
                request.Headers.Add("X-API-Key", _apiKey);

            // Đọc PDF nhiều câu -> lâu hơn embed; cho timeout riêng rộng hơn.
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(180));

            using var response = await client.SendAsync(request, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("TutorAI extract-pdf trả status {StatusCode}.", (int)response.StatusCode);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cts.Token);
            var result = JsonSerializer.Deserialize<ExtractPdfResponse>(content, _jsonOptions);
            if (result?.Questions == null)
            {
                _logger.LogWarning("TutorAI extract-pdf trả null questions: {Error}", result?.Error);
                return null;
            }

            return result.Questions
                .Select(q => new AiExtractedQuestion(q.Content, q.Solution, q.ProblemType, q.Chapter, q.Page, q.Images ?? new()))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lỗi gọi TutorAI extract-pdf.");
            return null;
        }
    }

    // Knowledge Base: forward thuần sang tutora-ai
    public async Task<KbUploadResult?> KbUploadAsync(
        byte[] fileBytes, string fileName, string? uploadedBy,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(ServiceKeys.HttpClients.TutorAi);

            using var form = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue(ContentTypeForFile(fileName));
            form.Add(fileContent, "file", fileName);
            if (!string.IsNullOrWhiteSpace(uploadedBy))
                form.Add(new StringContent(uploadedBy), "uploaded_by");

            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/kb/upload")
            {
                Content = form
            };
            if (!string.IsNullOrWhiteSpace(_apiKey))
                request.Headers.Add("X-API-Key", _apiKey);

            // Extract + chunk + embed nhiều đoạn -> có thể lâu; cho timeout rộng như extract-pdf.
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(180));

            using var response = await client.SendAsync(request, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("TutorAI kb/upload trả status {StatusCode}.", (int)response.StatusCode);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cts.Token);
            var result = JsonSerializer.Deserialize<KbUploadResponse>(content, _jsonOptions);
            if (result?.DocumentId == null)
            {
                _logger.LogWarning("TutorAI kb/upload trả null document_id.");
                return null;
            }
            return new KbUploadResult(result.DocumentId, result.ChunkCount, result.FileName ?? fileName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lỗi gọi TutorAI kb/upload.");
            return null;
        }
    }

    public async Task<int?> KbUpdateContentAsync(string documentId, string content, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(ServiceKeys.HttpClients.TutorAi);

            using var request = new HttpRequestMessage(
                HttpMethod.Put, $"/api/v1/kb/documents/{Uri.EscapeDataString(documentId)}/content")
            {
                Content = JsonContent.Create(new { content }),
            };
            if (!string.IsNullOrWhiteSpace(_apiKey))
                request.Headers.Add("X-API-Key", _apiKey);

            // Chunk lại + re-embed -> có thể lâu; timeout rộng như upload.
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(180));

            using var response = await client.SendAsync(request, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("TutorAI kb update-content trả status {StatusCode}.", (int)response.StatusCode);
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(cts.Token);
            var result = JsonSerializer.Deserialize<KbUploadResponse>(body, _jsonOptions);
            return result?.ChunkCount;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lỗi gọi TutorAI kb update-content.");
            return null;
        }
    }

    private static string ContentTypeForFile(string fileName)
    {
        var ext = System.IO.Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".pdf" => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            _ => "application/octet-stream",
        };
    }

    // Internal request/response shapes

    public async Task<List<AiSimilarQuestion>> FindSimilarQuestionsAsync(
        string text,
        string? chapter,
        string? difficulty,
        IReadOnlyList<Guid> excludeIds,
        int topK,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(ServiceKeys.HttpClients.TutorAi);
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/similar-questions")
            {
                Content = JsonContent.Create(new
                {
                    text,
                    chapter,
                    difficulty,
                    exclude_ids = excludeIds.Select(x => x.ToString()).ToList(),
                    top_k = topK,
                })
            };
            if (!string.IsNullOrWhiteSpace(_apiKey))
                request.Headers.Add("X-API-Key", _apiKey);

            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("TutorAI similar-questions trả {StatusCode}.", (int)response.StatusCode);
                return new();
            }

            var rows = await response.Content.ReadFromJsonAsync<List<SimilarQuestionDto>>(
                cancellationToken: cancellationToken);

            return rows?.Select(r => new AiSimilarQuestion(
                r.Id, r.Content, r.Solution, r.Chapter, r.Difficulty, r.Similarity)).ToList() ?? new();
        }
        catch (Exception ex)
        {
            // Không có bài luyện KHÔNG được làm hỏng việc giải bài.
            _logger.LogWarning(ex, "Gọi TutorAI similar-questions thất bại.");
            return new();
        }
    }

    private sealed record SimilarQuestionDto(
        Guid Id, string Content, string? Solution,
        string? Chapter, string? Difficulty, float Similarity);

    private sealed class KbUploadResponse
    {
        [JsonPropertyName("document_id")]
        public string? DocumentId { get; set; }

        [JsonPropertyName("chunk_count")]
        public int ChunkCount { get; set; }

        [JsonPropertyName("file_name")]
        public string? FileName { get; set; }
    }

    private sealed class ExtractPdfResponse
    {
        [JsonPropertyName("total")]
        public int Total { get; set; }

        [JsonPropertyName("questions")]
        public List<ExtractedQuestionItem>? Questions { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }

    private sealed class ExtractedQuestionItem
    {
        [JsonPropertyName("content")]
        public string Content { get; set; } = "";

        [JsonPropertyName("solution")]
        public string? Solution { get; set; }

        [JsonPropertyName("problem_type")]
        public string? ProblemType { get; set; }

        [JsonPropertyName("chapter")]
        public string? Chapter { get; set; }

        [JsonPropertyName("page")]
        public int? Page { get; set; }

        [JsonPropertyName("images")]
        public List<string>? Images { get; set; }
    }

    private sealed class EmbedRequest
    {
        [JsonPropertyName("items")]
        public List<EmbedItem> Items { get; set; } = new();
    }

    private sealed class EmbedItem
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("text")]
        public string Text { get; set; } = "";
    }

    private sealed class EmbedResponse
    {
        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("dim")]
        public int Dim { get; set; }

        [JsonPropertyName("results")]
        public List<EmbedResultItem>? Results { get; set; }
    }

    private sealed class EmbedResultItem
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("embedding")]
        public List<float>? Embedding { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }

    private sealed class AiRankRequest
    {
        [JsonPropertyName("query")]
        public string? Query { get; set; }

        [JsonPropertyName("candidate_ids")]
        public List<string> CandidateIds { get; set; } = new();

        [JsonPropertyName("top_k")]
        public int TopK { get; set; }
    }

    private sealed class AiRankResponse
    {
        [JsonPropertyName("results")]
        public List<AiRankResultItem>? Results { get; set; }

        [JsonPropertyName("total")]
        public int Total { get; set; }
    }

    private sealed class AiRankResultItem
    {
        [JsonPropertyName("tutor_id")]
        public string TutorId { get; set; } = "";

        [JsonPropertyName("similarity")]
        public float Similarity { get; set; }
    

}

    // Bài tập nhanh trong buổi học
    public async Task<AiMaterialExtraction?> ExtractMaterialAsync(
        byte[] fileBytes,
        string fileName,
        string? subject = null,
        string? grade = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(ServiceKeys.HttpClients.TutorAi);

            using var form = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue(ContentTypeForFile(fileName));
            form.Add(fileContent, "file", fileName);
            if (!string.IsNullOrWhiteSpace(subject))
                form.Add(new StringContent(subject), "subject");
            if (!string.IsNullOrWhiteSpace(grade))
                form.Add(new StringContent(grade), "grade");

            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/materials/extract")
            {
                Content = form
            };
            if (!string.IsNullOrWhiteSpace(_apiKey))
                request.Headers.Add("X-API-Key", _apiKey);

            // Tài liệu dài + có thể phải OCR ảnh -> timeout rộng như extract-pdf.
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(180));

            using var response = await client.SendAsync(request, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("TutorAI materials/extract trả status {StatusCode}.", (int)response.StatusCode);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cts.Token);
            var result = JsonSerializer.Deserialize<MaterialExtractResponse>(content, _jsonOptions);
            if (string.IsNullOrWhiteSpace(result?.FullText))
            {
                _logger.LogWarning("TutorAI materials/extract không đọc được nội dung: {Error}", result?.Error);
                return null;
            }

            return new AiMaterialExtraction(
                result.FullText, result.PageCount, result.Relevant, result.RejectReason);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lỗi gọi TutorAI materials/extract.");
            return null;
        }
    }

    public async Task<AiGeneratedPractice?> GeneratePracticeAsync(
        IReadOnlyList<AiMaterialSource> materials,
        string prompt,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(ServiceKeys.HttpClients.TutorAi);

            var payload = new GeneratePracticeRequestBody
            {
                Prompt = prompt,
                Materials = materials
                    .Select(m => new GeneratePracticeMaterial
                    {
                        MaterialId = m.MaterialId,
                        Title = m.Title,
                        FullText = m.FullText,
                    })
                    .ToList(),
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/practice/generate")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(payload, _jsonOptions),
                    System.Text.Encoding.UTF8,
                    "application/json"),
            };
            if (!string.IsNullOrWhiteSpace(_apiKey))
                request.Headers.Add("X-API-Key", _apiKey);

            // Gia sư đang đứng lớp chờ -> không để treo quá lâu. 90s là trần chấp nhận được.
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(90));

            using var response = await client.SendAsync(request, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("TutorAI practice/generate trả status {StatusCode}.", (int)response.StatusCode);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cts.Token);
            var result = JsonSerializer.Deserialize<GeneratePracticeResponse>(content, _jsonOptions);
            if (result?.Questions == null || result.Questions.Count == 0)
            {
                _logger.LogWarning("TutorAI practice/generate không sinh được câu nào: {Error}", result?.Error);
                // Có lý do cụ thể (AI từ chối yêu cầu) -> trả về để hiện cho gia sư.
                return string.IsNullOrWhiteSpace(result?.Error)
                    ? null
                    : new AiGeneratedPractice(string.Empty, new List<AiGeneratedQuestion>(), result.Error);
            }

            var questions = result.Questions
                .Select(q => new AiGeneratedQuestion(
                    q.Format ?? "mc",
                    q.Content ?? string.Empty,
                    q.Options?.Select(o => new AiAnswerOption(o.Key ?? string.Empty, o.Text ?? string.Empty)).ToList(),
                    q.CorrectAnswer,
                    q.Explanation,
                    q.SourceMaterialId,
                    q.SourcePage))
                .ToList();

            return new AiGeneratedPractice(result.Title ?? "Bài tập", questions, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lỗi gọi TutorAI practice/generate.");
            return null;
        }
    }

    private sealed class MaterialExtractResponse
    {
        [JsonPropertyName("full_text")]
        public string? FullText { get; set; }

        [JsonPropertyName("page_count")]
        public int? PageCount { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonPropertyName("relevant")]
        public bool? Relevant { get; set; }

        [JsonPropertyName("reject_reason")]
        public string? RejectReason { get; set; }
    }

    private sealed class GeneratePracticeRequestBody
    {
        [JsonPropertyName("prompt")]
        public string Prompt { get; set; } = "";

        [JsonPropertyName("materials")]
        public List<GeneratePracticeMaterial> Materials { get; set; } = new();
    }

    private sealed class GeneratePracticeMaterial
    {
        [JsonPropertyName("material_id")]
        public int MaterialId { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        [JsonPropertyName("full_text")]
        public string FullText { get; set; } = "";
    }

    private sealed class GeneratePracticeResponse
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("questions")]
        public List<GeneratedQuestionItem>? Questions { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }

    private sealed class GeneratedQuestionItem
    {
        [JsonPropertyName("format")]
        public string? Format { get; set; }

        [JsonPropertyName("content")]
        public string? Content { get; set; }

        [JsonPropertyName("options")]
        public List<GeneratedOptionItem>? Options { get; set; }

        [JsonPropertyName("correct_answer")]
        public string? CorrectAnswer { get; set; }

        [JsonPropertyName("explanation")]
        public string? Explanation { get; set; }

        [JsonPropertyName("source_material_id")]
        public int? SourceMaterialId { get; set; }

        [JsonPropertyName("source_page")]
        public int? SourcePage { get; set; }
    }

    private sealed class GeneratedOptionItem
    {
        [JsonPropertyName("key")]
        public string? Key { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }

}
