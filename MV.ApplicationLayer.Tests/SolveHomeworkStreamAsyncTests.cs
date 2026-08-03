using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MV.ApplicationLayer.Services;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using MV.InfrastructureLayer.DBContext;
using MV.InfrastructureLayer.Repositories;
using Xunit;

namespace MV.ApplicationLayer.Tests;

// Maps to Excel sheet "SolveHomeworkStreamAsync" (Code_39, AiChatService.SolveStreamAsync).
// Not actually Postgres-blocked - no FromSqlRaw/ExecuteUpdateAsync anywhere in this method.
// The real barrier is the outbound HTTP call to the Tutora-AI service, faked here with a
// custom HttpMessageHandler that returns a canned SSE body instead of hitting a real service.
public class SolveHomeworkStreamAsyncTests
{
    private const string UserId = "student-user-1";
    private static readonly Guid SessionId = Guid.NewGuid();

    [Fact]
    public async Task OwnedSession_StreamsAndPersistsUserAndAssistantMessages()
    {
        var db = TestSupport.CreateInMemoryContext("solve-homework-stream");
        db.ChatSessions.Add(new ChatSession { SessionId = SessionId, UserId = UserId, SessionType = "homework", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var sseBody = "data: {\"delta\":\"Đáp án là \"}\n\ndata: {\"delta\":\"x = 2.\"}\n\n";
        var service = CreateService(db, new FakeHttpMessageHandler(HttpStatusCode.OK, sseBody));
        var request = new AiSolveRequest { Text = "Giải x - 2 = 0", Grade = "9" };

        var chunks = new List<string>();
        await foreach (var line in service.SolveStreamAsync(UserId, SessionId, request))
            chunks.Add(line);

        Assert.Equal(2, chunks.Count);
        var messages = db.ChatHistories.Where(m => m.SessionId == SessionId).OrderBy(m => m.CreatedAt).ToList();
        Assert.Equal(2, messages.Count);
        Assert.Equal("Giải x - 2 = 0", messages[0].Content);
        Assert.Equal("Đáp án là x = 2.", messages[1].Content);
    }

    [Fact]
    public async Task SessionNotOwnedByCaller_ThrowsForbidden()
    {
        var db = TestSupport.CreateInMemoryContext("solve-homework-stream");
        db.ChatSessions.Add(new ChatSession { SessionId = SessionId, UserId = "someone-else", SessionType = "homework", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var service = CreateService(db, new FakeHttpMessageHandler(HttpStatusCode.OK, ""));
        var request = new AiSolveRequest { Text = "Giải x - 2 = 0", Grade = "9" };

        await Assert.ThrowsAsync<AiChatSessionForbiddenException>(async () =>
        {
            await foreach (var _ in service.SolveStreamAsync(UserId, SessionId, request)) { }
        });
    }

    [Fact]
    public async Task UnknownSession_ThrowsNotFound()
    {
        var db = TestSupport.CreateInMemoryContext("solve-homework-stream");
        var service = CreateService(db, new FakeHttpMessageHandler(HttpStatusCode.OK, ""));
        var request = new AiSolveRequest { Text = "Giải x - 2 = 0", Grade = "9" };

        await Assert.ThrowsAsync<AiChatSessionNotFoundException>(async () =>
        {
            await foreach (var _ in service.SolveStreamAsync(UserId, Guid.NewGuid(), request)) { }
        });
    }

    [Fact]
    public async Task AiServiceReturnsError_ThrowsHttpRequestException()
    {
        var db = TestSupport.CreateInMemoryContext("solve-homework-stream");
        db.ChatSessions.Add(new ChatSession { SessionId = SessionId, UserId = UserId, SessionType = "homework", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var service = CreateService(db, new FakeHttpMessageHandler(HttpStatusCode.BadGateway, ""));
        var request = new AiSolveRequest { Text = "Giải x - 2 = 0", Grade = "9" };

        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await foreach (var _ in service.SolveStreamAsync(UserId, SessionId, request)) { }
        });
    }

    [Fact]
    public async Task CancelledMidStream_ThrowsOperationCanceledButPersistsPartialAssistantMessage()
    {
        var db = TestSupport.CreateInMemoryContext("solve-homework-stream");
        db.ChatSessions.Add(new ChatSession { SessionId = SessionId, UserId = UserId, SessionType = "homework", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        // Delivered one SSE frame per read so the second read happens after the cancel below;
        // a fully-buffered body would be readable without ever observing the token.
        var service = CreateService(db, new ChunkedSseHandler(
            "data: {\"delta\":\"Buoc 1. \"}\n\n",
            "data: {\"delta\":\"Buoc 2.\"}\n\n"));
        var request = new AiSolveRequest { Text = "Giải x - 2 = 0", Grade = "9" };

        using var cts = new CancellationTokenSource();
        await using var enumerator = service.SolveStreamAsync(UserId, SessionId, request, cts.Token).GetAsyncEnumerator();

        Assert.True(await enumerator.MoveNextAsync());
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await enumerator.MoveNextAsync());

        var assistantMessage = db.ChatHistories.SingleOrDefault(m => m.SessionId == SessionId && m.Role == ChatHistoryRole.Assistant);
        Assert.NotNull(assistantMessage);
        Assert.Equal("Buoc 1. ", assistantMessage!.Content);
    }

    [Fact]
    public async Task WithBase64Image_UploadsImageAndSetsUrlOnUserMessage()
    {
        var db = TestSupport.CreateInMemoryContext("solve-homework-stream");
        db.ChatSessions.Add(new ChatSession { SessionId = SessionId, UserId = UserId, SessionType = "homework", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var sseBody = "data: {\"delta\":\"Đây là hình tam giác vuông.\"}\n\n";
        var service = CreateService(db, new FakeHttpMessageHandler(HttpStatusCode.OK, sseBody));
        var imageBase64 = "data:image/png;base64," + Convert.ToBase64String(new byte[] { 1, 2, 3 });
        var request = new AiSolveRequest { ImageBase64 = imageBase64, Grade = "9" };

        await foreach (var _ in service.SolveStreamAsync(UserId, SessionId, request)) { }

        var userMessage = db.ChatHistories.Single(m => m.SessionId == SessionId && m.Role == ChatHistoryRole.User);
        Assert.False(string.IsNullOrEmpty(userMessage.ImageUrl));
        Assert.Equal("[hình ảnh]", userMessage.Content);
    }

    private static AiChatService CreateService(AgoraDbContext db, HttpMessageHandler handler)
    {
        var configuration = new ConfigurationBuilder().Build();
        return new AiChatService(
            new AiChatRepository(db),
            null!,
            null!,
            new FakeHttpClientFactory(handler),
            configuration,
            new FakeFileStorageService(),
            new FakeAiCreditService(),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<AiChatService>.Instance);
    }

    private sealed class FakeHttpMessageHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode) { Content = new StringContent(body) };
            return Task.FromResult(response);
        }
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler) { BaseAddress = new Uri("https://tutora-ai.test") };
    }

    private sealed class ChunkedSseHandler(params string[] frames) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new ChunkedStream(frames))
            });
    }

    // Hands back exactly one frame per ReadAsync and honours the caller's token, so a mid-stream
    // cancel surfaces the same way a real server-sent-events connection would.
    private sealed class ChunkedStream(string[] frames) : Stream
    {
        private int _index;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            if (_index >= frames.Length) return 0;
            var bytes = System.Text.Encoding.UTF8.GetBytes(frames[_index++]);
            bytes.CopyTo(buffer.Span);
            return bytes.Length;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

internal sealed class FakeAiCreditService : IAiCreditService
{
    public Task<int> GrantAsync(string userId, int amount, string source, string? referenceId, string? description, CancellationToken ct = default) => Task.FromResult(0);
    public Task<int> SpendAsync(string userId, int amount, string? referenceId, string? description, CancellationToken ct = default) => Task.FromResult(0);
    public Task GrantFreePackageAsync(string userId, CancellationToken ct = default) => Task.CompletedTask;
    public Task GrantBookingBonusAsync(string userId, int bookingId, CancellationToken ct = default) => Task.CompletedTask;
    public Task<AiCreditBalanceResponse> GetBalanceAsync(string userId, CancellationToken ct = default) => Task.FromResult(new AiCreditBalanceResponse { Balance = 10 });
    public Task<IReadOnlyList<AiCreditTransactionResponse>> GetHistoryAsync(string userId, int take, CancellationToken ct = default) => Task.FromResult((IReadOnlyList<AiCreditTransactionResponse>)new List<AiCreditTransactionResponse>());
    public Task<IReadOnlyList<AiCreditPackageResponse>> GetActivePackagesAsync(CancellationToken ct = default) => Task.FromResult((IReadOnlyList<AiCreditPackageResponse>)new List<AiCreditPackageResponse>());
    public Task<AiCreditPurchaseResponse> InitiatePurchaseAsync(string buyerUserId, AiCreditPurchaseRequest request, CancellationToken ct = default) => throw new NotImplementedException();
    public Task CompletePurchaseAsync(PaymentWebhookRequest webhook, string? rawPayload, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<AiCreditPurchaseStatusResponse> GetPurchaseStatusAsync(string userId, long orderCode, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<IReadOnlyList<AiCreditPackageResponse>> AdminGetPackagesAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task<AiCreditPackageResponse> AdminCreatePackageAsync(AiCreditPackageCreateRequest request, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<AiCreditPackageResponse> AdminUpdatePackageAsync(int packageId, AiCreditPackageUpdateRequest request, CancellationToken ct = default) => throw new NotImplementedException();
    public Task AdminDeletePackageAsync(int packageId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<AiCreditPackageResponse> AdminUploadIconAsync(int packageId, Microsoft.AspNetCore.Http.IFormFile file, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<int> AdminGetBookingBonusAsync(CancellationToken ct = default) => Task.FromResult(0);
    public Task AdminSetBookingBonusAsync(int amount, string? updatedByUserId, CancellationToken ct = default) => Task.CompletedTask;
}
