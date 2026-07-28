using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MV.DomainLayer.Configuration;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer.DBContext;
using MV.PresentationLayer.Controllers;
using Npgsql;
using Xunit;

namespace MV.ApplicationLayer.Tests;

public class AgoraWebhookControllerTests
{
    // Environments.* are static readonly, so they cannot be used in attributes or default parameters.
    private const string DevelopmentEnvironment = "Development";
    private const string ProductionEnvironment = "Production";
    private const string StagingEnvironment = "Staging";
    private const string Secret = "agora-ncs-test-secret";
    private const string NoticeIdUniqueConstraint = "ux_agora_events_notice";
    private const string ValidNoticeId = "notice-valid-001";
    private const long EventTimestamp = 1_735_689_600;

    private const string ValidRtcBody = """
        {
          "noticeId": "notice-valid-001",
          "productId": 1,
          "eventType": 107,
          "payload": {
            "channelName": "321",
            "uid": "student-42",
            "platform": 1,
            "ts": 1735689600
          }
        }
        """;

    [Fact]
    public async Task ValidSignature_InsertsMappedEventAndReturnsJsonAcknowledgement()
    {
        await using var db = CreateContext();
        var controller = CreateController(db, ValidRtcBody, Sign(ValidRtcBody));

        var result = Assert.IsType<OkObjectResult>(await controller.HandleNcsWebhook());

        Assert.NotNull(result.Value);
        var channelEvent = Assert.Single(await db.AgoraChannelEvents.ToListAsync());
        Assert.Equal(ValidNoticeId, channelEvent.NoticeId);
        Assert.Equal(321, channelEvent.ClassSessionId);
        Assert.Equal((short)107, channelEvent.EventType);
        Assert.Equal(
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            channelEvent.EventAt);
        Assert.Equal(DateTimeKind.Utc, channelEvent.EventAt.Kind);

        using var payload = JsonDocument.Parse(channelEvent.Payload);
        Assert.Equal("321", payload.RootElement.GetProperty("channelName").GetString());
        Assert.Equal(JsonValueKind.String, payload.RootElement.GetProperty("uid").ValueKind);
        Assert.Equal("student-42", payload.RootElement.GetProperty("uid").GetString());
        Assert.Equal(EventTimestamp, payload.RootElement.GetProperty("ts").GetInt64());
    }

    [Fact]
    public async Task InvalidSignature_ReturnsUnauthorizedWithoutInsert()
    {
        await using var db = CreateContext();
        var invalidSignature = new string('0', 64);
        var controller = CreateController(db, ValidRtcBody, invalidSignature);

        var result = await controller.HandleNcsWebhook();

        Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Empty(await db.AgoraChannelEvents.ToListAsync());
    }

    [Fact]
    public async Task SignatureVerification_UsesExactRequestBytes()
    {
        await using var db = CreateContext();
        var rawBody = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(ValidRtcBody))
            .ToArray();
        var controller = CreateController(db, rawBody, Sign(rawBody));

        var result = Assert.IsType<OkObjectResult>(await controller.HandleNcsWebhook());

        Assert.NotNull(result.Value);
        Assert.Single(await db.AgoraChannelEvents.ToListAsync());
    }

    [Fact]
    public async Task DuplicateNoticeId_ReturnsJsonAcknowledgementAndKeepsOneRow()
    {
        const string duplicateNoticeId = "notice-duplicate-001";
        await using var db = CreateContext();
        db.AgoraChannelEvents.Add(new AgoraChannelEvent
        {
            NoticeId = duplicateNoticeId,
            ClassSessionId = 321,
            EventType = 107,
            EventAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ReceivedAt = DateTime.UtcNow,
            Payload = "{}"
        });
        await db.SaveChangesAsync();

        db.SaveFailure = CreateUniqueNoticeIdException();
        var duplicateBody = ValidRtcBody.Replace(ValidNoticeId, duplicateNoticeId, StringComparison.Ordinal);
        var controller = CreateController(db, duplicateBody, Sign(duplicateBody));

        var result = Assert.IsType<OkObjectResult>(await controller.HandleNcsWebhook());

        Assert.NotNull(result.Value);
        Assert.Equal(1, await db.AgoraChannelEvents.CountAsync());
        Assert.Equal(duplicateNoticeId, (await db.AgoraChannelEvents.SingleAsync()).NoticeId);
    }

    [Theory]
    [InlineData(ProductionEnvironment)]
    [InlineData(StagingEnvironment)]
    public async Task VerificationNotConfiguredOutsideDevelopment_ReturnsUnauthorizedWithoutInsert(
        string environmentName)
    {
        await using var db = CreateContext();
        var controller = CreateController(
            db,
            ValidRtcBody,
            signature: null,
            settings: new AgoraNotificationSettings { Enabled = false, Secret = string.Empty },
            environmentName: environmentName);

        var result = await controller.HandleNcsWebhook();

        Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Empty(await db.AgoraChannelEvents.ToListAsync());
    }

    [Fact]
    public async Task EnabledWithoutSecretOutsideDevelopment_ReturnsUnauthorizedWithoutInsert()
    {
        await using var db = CreateContext();
        var controller = CreateController(
            db,
            ValidRtcBody,
            Sign(ValidRtcBody),
            settings: new AgoraNotificationSettings { Enabled = true, Secret = string.Empty },
            environmentName: ProductionEnvironment);

        var result = await controller.HandleNcsWebhook();

        Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Empty(await db.AgoraChannelEvents.ToListAsync());
    }

    [Fact]
    public async Task VerificationNotConfiguredInDevelopment_StoresEventWithoutSignature()
    {
        await using var db = CreateContext();
        var controller = CreateController(
            db,
            ValidRtcBody,
            signature: null,
            settings: new AgoraNotificationSettings { Enabled = false, Secret = string.Empty },
            environmentName: DevelopmentEnvironment);

        var result = Assert.IsType<OkObjectResult>(await controller.HandleNcsWebhook());

        Assert.NotNull(result.Value);
        Assert.Single(await db.AgoraChannelEvents.ToListAsync());
    }

    [Fact]
    public async Task NonRtcProduct_ReturnsJsonAcknowledgementWithoutInsert()
    {
        const string body = """
            {
              "noticeId": "notice-non-rtc-001",
              "productId": 2,
              "eventType": 107,
              "payload": { "ts": 1735689600 }
            }
            """;
        await using var db = CreateContext();
        var controller = CreateController(db, body, Sign(body));

        var result = Assert.IsType<OkObjectResult>(await controller.HandleNcsWebhook());

        Assert.NotNull(result.Value);
        Assert.Empty(await db.AgoraChannelEvents.ToListAsync());
    }

    [Fact]
    public async Task MalformedJson_ReturnsJsonAcknowledgementWithoutInsert()
    {
        const string body = "{ not-json }";
        await using var db = CreateContext();
        var controller = CreateController(db, body, Sign(body));

        var result = Assert.IsType<OkObjectResult>(await controller.HandleNcsWebhook());

        Assert.NotNull(result.Value);
        Assert.Empty(await db.AgoraChannelEvents.ToListAsync());
    }

    [Fact]
    public async Task NonDuplicateDatabaseFailure_PropagatesForProviderRetry()
    {
        await using var db = CreateContext();
        db.SaveFailure = new DbUpdateException("database unavailable");
        var controller = CreateController(db, ValidRtcBody, Sign(ValidRtcBody));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(
            () => controller.HandleNcsWebhook());

        Assert.Equal("database unavailable", exception.Message);
        Assert.Empty(await db.AgoraChannelEvents.ToListAsync());
    }

    private static TestAgoraDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AgoraDbContext>()
            .UseInMemoryDatabase($"agora-webhook-{Guid.NewGuid()}")
            .Options;
        return new TestAgoraDbContext(options);
    }

    private static AgoraWebhookController CreateController(
        TestAgoraDbContext db,
        string rawBody,
        string? signature,
        AgoraNotificationSettings? settings = null,
        string environmentName = DevelopmentEnvironment)
        => CreateController(db, Encoding.UTF8.GetBytes(rawBody), signature, settings, environmentName);

    private static AgoraWebhookController CreateController(
        TestAgoraDbContext db,
        byte[] rawBody,
        string? signature,
        AgoraNotificationSettings? settings = null,
        string environmentName = DevelopmentEnvironment)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Body = new MemoryStream(rawBody);
        if (signature is not null)
            httpContext.Request.Headers["Agora-Signature-V2"] = signature;

        return new AgoraWebhookController(
            db,
            NullLogger<AgoraWebhookController>.Instance,
            Options.Create(settings ?? new AgoraNotificationSettings
            {
                Enabled = true,
                Secret = Secret
            }),
            new FakeHostEnvironment(environmentName))
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    private sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "MV.ApplicationLayer.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static string Sign(string rawBody)
        => Sign(Encoding.UTF8.GetBytes(rawBody));

    private static string Sign(byte[] rawBody)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        return Convert.ToHexString(hmac.ComputeHash(rawBody))
            .ToLowerInvariant();
    }

    private static DbUpdateException CreateUniqueNoticeIdException()
    {
        var postgresException = new PostgresException(
            "duplicate key value violates unique constraint",
            "ERROR",
            "ERROR",
            PostgresErrorCodes.UniqueViolation,
            constraintName: NoticeIdUniqueConstraint);

        return new DbUpdateException("Could not save Agora channel event.", postgresException);
    }

    private sealed class TestAgoraDbContext(DbContextOptions<AgoraDbContext> options)
        : AgoraDbContext(options)
    {
        public DbUpdateException? SaveFailure { get; set; }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (SaveFailure is not null &&
                ChangeTracker.Entries<AgoraChannelEvent>()
                    .Any(entry => entry.State == EntityState.Added))
            {
                throw SaveFailure;
            }

            return base.SaveChangesAsync(cancellationToken);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<QuestionBank>().Ignore(question => question.Embedding);
            modelBuilder.Entity<TutoraKbChunk>().Ignore(chunk => chunk.Embedding);
        }
    }
}
