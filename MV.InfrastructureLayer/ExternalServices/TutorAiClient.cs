using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;

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

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public TutorAiClient(IHttpClientFactory httpClientFactory, ILogger<TutorAiClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
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

            using var response = await client.PostAsJsonAsync(
                "/api/v1/tutors/recommend",
                requestBody,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "TutorAI service returned non-success status {StatusCode}. Falling back to SQL order.",
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

    // Internal request/response shapes

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
}
