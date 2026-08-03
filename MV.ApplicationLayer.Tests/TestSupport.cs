using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Distributed;
using MV.ApplicationLayer.RepositoryInterfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer.DBContext;

namespace MV.ApplicationLayer.Tests;

// Shared test doubles/helpers reused across the Excel-spec test files. Hand-rolled instead of
// a mocking library (none referenced in this project) - each fake implements only what the
// exercised code paths actually call.
internal static class TestSupport
{
    public static Microsoft.AspNetCore.Http.IFormFile FakeFormFile(string fileName, long sizeBytes = 1024)
    {
        var stream = new MemoryStream(new byte[sizeBytes]);
        return new Microsoft.AspNetCore.Http.FormFile(stream, 0, sizeBytes, "file", fileName)
        {
            // FormFile.ContentType reads from Headers - null Headers throws NullReferenceException
            // the moment any code reads .ContentType (e.g. DisputeService.UploadTutorDisputeEvidenceAsync).
            Headers = new Microsoft.AspNetCore.Http.HeaderDictionary { ["Content-Type"] = "image/jpeg" }
        };
    }

    public static AgoraDbContext CreateInMemoryContext(string dbNamePrefix)
    {
        // Several services call Database.BeginTransactionAsync() for real-DB atomicity, which is
        // meaningless (and a thrown warning-as-error by default) on the InMemory provider - ignore it.
        var options = new DbContextOptionsBuilder<AgoraDbContext>()
            .UseInMemoryDatabase($"{dbNamePrefix}-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new EmbeddingFreeDbContext(options);
    }

    // AgoraDbContext.OnModelCreating configures pgvector columns (QuestionBank/TutoraKbChunk
    // Embedding) that the InMemory provider can't map - every InMemory-backed test needs this override.
    private sealed class EmbeddingFreeDbContext(DbContextOptions<AgoraDbContext> options) : AgoraDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<QuestionBank>().Ignore(x => x.Embedding);
            modelBuilder.Entity<TutoraKbChunk>().Ignore(x => x.Embedding);
        }
    }
}

internal sealed class FakeOtpSender : IOtpSender
{
    public Task SendOtpAsync(string phone, string otpCode) => Task.CompletedTask;
}

internal sealed class FakeDistributedCache : IDistributedCache
{
    private readonly Dictionary<string, byte[]> _store = new();

    public byte[]? Get(string key) => _store.TryGetValue(key, out var v) ? v : null;
    public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult(Get(key));
    public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => _store[key] = value;
    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
    {
        Set(key, value, options);
        return Task.CompletedTask;
    }
    public void Refresh(string key) { }
    public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
    public void Remove(string key) => _store.Remove(key);
    public Task RemoveAsync(string key, CancellationToken token = default)
    {
        Remove(key);
        return Task.CompletedTask;
    }
}

// Records every call instead of no-op'ing, so tests can assert notifications were (or weren't) sent.
internal sealed class FakeNotificationService : INotificationService
{
    public List<NotificationRequest> SentSingle { get; } = new();
    public List<NotificationRequest> SentBatch { get; } = new();

    public Task<StatusResponse> CreateNotificationAsync(NotificationRequest request)
    {
        SentSingle.Add(request);
        return Task.FromResult(new StatusResponse { Status = "success" });
    }

    public Task<StatusResponse> CreateNotificationsAsync(IEnumerable<NotificationRequest> requests)
    {
        SentBatch.AddRange(requests);
        return Task.FromResult(new StatusResponse { Status = "success" });
    }

    public Task<NotificationResponse?> GetNotificationByIdAsync(int notificationId) => Task.FromResult<NotificationResponse?>(null);
    public Task<IEnumerable<NotificationResponse>> GetNotificationsByUserIdAsync(string userId) => Task.FromResult<IEnumerable<NotificationResponse>>(Array.Empty<NotificationResponse>());
    public Task<IEnumerable<NotificationResponse>> GetUnreadNotificationsByUserIdAsync(string userId) => Task.FromResult<IEnumerable<NotificationResponse>>(Array.Empty<NotificationResponse>());
    public Task<int> GetUnreadCountByUserIdAsync(string userId) => Task.FromResult(0);
    public Task<UnreadCountResponse> GetUnreadCountResponseByUserIdAsync(string userId) => Task.FromResult(new UnreadCountResponse());
    public Task<IEnumerable<NotificationResponse>> GetAllNotificationsAsync() => Task.FromResult<IEnumerable<NotificationResponse>>(Array.Empty<NotificationResponse>());
    public Task<StatusResponse> MarkAsReadAsync(int notificationId, string currentUserId) => Task.FromResult(new StatusResponse { Status = "success" });
    public Task<StatusResponse> MarkAllAsReadAsync(string userId) => Task.FromResult(new StatusResponse { Status = "success" });
    public Task<StatusResponse> MarkAsReadByTypeAsync(string userId, string type) => Task.FromResult(new StatusResponse { Status = "success" });
    public Task<StatusResponse> DeleteNotificationAsync(int notificationId, string currentUserId) => Task.FromResult(new StatusResponse { Status = "success" });
    public Task<StatusResponse> DeleteAllNotificationsByUserIdAsync(string userId) => Task.FromResult(new StatusResponse { Status = "success" });
    public Task<StatusResponse> DeleteOldNotificationsAsync(int daysOld) => Task.FromResult(new StatusResponse { Status = "success" });
}

internal sealed class FakeEncryptionService : IEncryptionService
{
    public string Encrypt(string plaintext) => "enc:" + plaintext;
    public string? Decrypt(string? ciphertext) =>
        string.IsNullOrEmpty(ciphertext) ? null : ciphertext.StartsWith("enc:") ? ciphertext[4..] : ciphertext;
}

internal sealed class FakeFptAiService : IFptAiService
{
    public MV.DomainLayer.DTO.ResponseModel.FptAiIdCardResponse? IdCardResponseToReturn { get; set; }
    public bool ThrowOnVerifyIdCard { get; set; }

    public Task<MV.DomainLayer.DTO.ResponseModel.FptAiIdCardResponse> VerifyIdCardAsync(Stream imageStream, string fileName)
    {
        if (ThrowOnVerifyIdCard)
            throw new InvalidOperationException("FPT.AI unreachable (simulated)");
        return Task.FromResult(IdCardResponseToReturn ?? new MV.DomainLayer.DTO.ResponseModel.FptAiIdCardResponse { Data = new List<MV.DomainLayer.DTO.ResponseModel.FptAiResult>() });
    }

    public Task<MV.DomainLayer.DTO.ResponseModel.FptAiLivenessResponse> CheckVideoLivenessAsync(Stream videoStream, string fileName)
        => throw new NotImplementedException();

    public Task<MV.DomainLayer.DTO.ResponseModel.TextModerationResponse> CheckTextContentSafeAsync(string textContent)
        => throw new NotImplementedException();
}

internal sealed class FakeFileStorageService : IFileStorageService
{
    public Task EnsureBucketExistsAsync(string bucketName) => Task.CompletedTask;
    public Task<string> UploadFileAsync(string bucketName, string userId, Microsoft.AspNetCore.Http.IFormFile file) => Task.FromResult($"https://fake-storage.local/{bucketName}/{userId}/{file.FileName}");
    public Task<string> UploadImageBytesAsync(string bucketName, string userId, byte[] bytes, string fileName) => Task.FromResult($"https://fake-storage.local/{bucketName}/{userId}/{fileName}");
    public Task<string> UploadPrivateFileAsync(string bucketName, string userId, Microsoft.AspNetCore.Http.IFormFile file) => Task.FromResult($"private-id/{bucketName}/{userId}/{file.FileName}");
    public string GenerateSignedUrl(string publicIdOrUrl, int expiresInMinutes = 15) => $"https://fake-storage.local/signed/{publicIdOrUrl}";
    public Task<bool> DeleteFileAsync(string bucketName, string userId, string filePathOrUrl) => Task.FromResult(true);
}

internal sealed class FakeTutorProfileUpdateStagingService : ITutorProfileUpdateStagingService
{
    private readonly Dictionary<string, (MV.DomainLayer.DTO.PendingTutorProfileUpdate Data, string RawJson)> _store = new();

    // When set, simulates a tutor submitting a newer edit in the gap between the admin's read
    // (GetPendingUpdateWithRawAsync) and the later compare-and-delete call.
    public (MV.DomainLayer.DTO.PendingTutorProfileUpdate Data, string RawJson)? MutateAfterNextReadTo { get; set; }

    public void Seed(string tutorId, MV.DomainLayer.DTO.PendingTutorProfileUpdate data, string? rawJson = null) =>
        _store[tutorId] = (data, rawJson ?? Guid.NewGuid().ToString());

    public Task<MV.DomainLayer.DTO.PendingTutorProfileUpdate?> GetPendingUpdateAsync(string tutorId) =>
        Task.FromResult(_store.TryGetValue(tutorId, out var v) ? v.Data : null);

    public Task<(MV.DomainLayer.DTO.PendingTutorProfileUpdate? Data, string? RawJson)> GetPendingUpdateWithRawAsync(string tutorId)
    {
        var result = _store.TryGetValue(tutorId, out var v) ? (v.Data, v.RawJson)! : ((MV.DomainLayer.DTO.PendingTutorProfileUpdate?)null, (string?)null);
        if (MutateAfterNextReadTo is { } mutation)
        {
            _store[tutorId] = mutation;
            MutateAfterNextReadTo = null;
        }
        return Task.FromResult(result);
    }

    public Task UpsertPendingUpdateAsync(string tutorId, Action<MV.DomainLayer.DTO.PendingTutorProfileUpdate> applyChanges)
    {
        var data = _store.TryGetValue(tutorId, out var existing) ? existing.Data : new MV.DomainLayer.DTO.PendingTutorProfileUpdate { TutorId = tutorId };
        applyChanges(data);
        _store[tutorId] = (data, Guid.NewGuid().ToString());
        return Task.CompletedTask;
    }

    public Task ClearPendingUpdateAsync(string tutorId)
    {
        _store.Remove(tutorId);
        return Task.CompletedTask;
    }

    public Task<bool> ClearPendingUpdateIfUnchangedAsync(string tutorId, string expectedRawJson)
    {
        if (_store.TryGetValue(tutorId, out var v) && v.RawJson == expectedRawJson)
        {
            _store.Remove(tutorId);
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    public Task<List<string>> GetAllPendingTutorIdsAsync() => Task.FromResult(_store.Keys.ToList());
}

internal sealed class FakeTutorEmbedQueue : ITutorEmbedQueue
{
    public List<string> Enqueued { get; } = new();
    public void Enqueue(string tutorId) => Enqueued.Add(tutorId);
    public async IAsyncEnumerable<string> DequeueAllAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        yield break;
    }
}

internal sealed class FakeTutorAiClient : ITutorAiClient
{
    public List<AiRankedTutor>? RankResultToReturn { get; set; }

    public Task<List<AiRankedTutor>?> RankAsync(string? query, IReadOnlyList<string> candidateIds, int topK, CancellationToken cancellationToken = default)
        => Task.FromResult(RankResultToReturn);

    public Task<float[]?> EmbedAsync(string id, string text, CancellationToken cancellationToken = default) => Task.FromResult<float[]?>(null);
    public Task EmbedTutorAsync(string tutorId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<List<AiExtractedQuestion>?> ExtractPdfAsync(byte[] pdfBytes, string fileName, CancellationToken cancellationToken = default) => Task.FromResult<List<AiExtractedQuestion>?>(null);
    public Task<KbUploadResult?> KbUploadAsync(byte[] fileBytes, string fileName, string? uploadedBy, CancellationToken cancellationToken = default) => Task.FromResult<KbUploadResult?>(null);
    public Task<int?> KbUpdateContentAsync(string documentId, string content, CancellationToken cancellationToken = default) => Task.FromResult<int?>(null);
}

internal sealed class FakeSessionPresenceService : ISessionPresenceService
{
    private readonly HashSet<(int, string)> _present = new();

    public void SetPresent(int classSessionId, string userId) => _present.Add((classSessionId, userId));
    public void Heartbeat(int classSessionId, string userId) => _present.Add((classSessionId, userId));
    public void Leave(int classSessionId, string userId) => _present.Remove((classSessionId, userId));
    public bool IsPresent(int classSessionId, string userId) => _present.Contains((classSessionId, userId));
    public void JoinLobby(int classSessionId, string userId, string connectionId) { }
    public (int ClassSessionId, string UserId)? RemoveLobbyConnection(string connectionId) => null;
    public (int ClassSessionId, string UserId)? GetLobbyEntry(string connectionId) => null;
    public bool IsInLobby(int classSessionId, string userId) => false;
}

internal sealed class FakeCloudRecordingService : ICloudRecordingService
{
    public bool Enabled => false;
    public Task<CloudRecordingHandle> StartAsync(int classSessionId, string channel, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<CloudRecordingResult> StopAsync(int classSessionId, string channel, string resourceId, string sid, CancellationToken ct = default) => throw new NotImplementedException();
}

internal sealed class FakeAuthenticationRepository : IAuthenticationRepository
{
    // Settable so tests exercising RefreshTokenAsync-style flows can control what an
    // "expired access token" decodes to; defaults to null (invalid token) for callers that don't care.
    public ClaimsPrincipal? PrincipalToReturn { get; set; }

    public string GenerateJwtToken(LoginResponse loginResponse) => $"fake-jwt-{loginResponse.Userid}";
    public string GenerateRefreshToken() => $"fake-refresh-{Guid.NewGuid()}";
    public string HashToken(string token) => $"hash-{token}";
    public ClaimsPrincipal? GetPrincipalFromExpiredToken(string token) => PrincipalToReturn;

    public static ClaimsPrincipal PrincipalFor(string userId) => new(
        new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId) }, "fake"));
}
