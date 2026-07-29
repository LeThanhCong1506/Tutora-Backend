using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MV.ApplicationLayer.Services;
using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace MV.ApplicationLayer.Tests;

public class DisputeClassificationServiceTests
{
    [Fact]
    public async Task ClassifyAsync_UsesStrictSchemaAndReturnsGroqClassification()
    {
        var handler = new CapturingHandler("""
            {"choices":[{"message":{"content":"{\"priority\":\"high\",\"reason\":\"Có dấu hiệu ảnh hưởng nghiêm trọng đến người học.\"}"}}]}
            """);
        using var client = new HttpClient(handler);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Groq:ApiKey"] = "test-key"
            })
            .Build();
        var service = new DisputeClassificationService(
            client,
            configuration,
            NullLogger<DisputeClassificationService>.Instance);

        var result = await service.ClassifyAsync("quality", "Gia sư có hành vi không phù hợp trong buổi học.");

        Assert.Equal("high", result.Priority);
        Assert.NotNull(handler.RequestBody);

        using var document = JsonDocument.Parse(handler.RequestBody!);
        var responseFormat = document.RootElement.GetProperty("response_format");
        Assert.Equal("json_schema", responseFormat.GetProperty("type").GetString());

        var schema = responseFormat.GetProperty("json_schema");
        Assert.True(schema.GetProperty("strict").GetBoolean());
        var definition = schema.GetProperty("schema");
        Assert.False(definition.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            new[] { "low", "medium", "high" },
            definition.GetProperty("properties").GetProperty("priority").GetProperty("enum")
                .EnumerateArray().Select(x => x.GetString()).ToArray());
    }

    private sealed class CapturingHandler(string responseJson) : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        }
    }
}
