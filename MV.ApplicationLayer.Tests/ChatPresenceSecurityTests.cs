using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.Http.Features;
using MV.ApplicationLayer.Hubs;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer.DBContext;
using MV.InfrastructureLayer.Repositories;
using MV.PresentationLayer.Controllers;
using Xunit;

namespace MV.ApplicationLayer.Tests;

public class ChatPresenceSecurityTests
{
    [Fact]
    public async Task ChatHub_RejectsEveryChannelOperationBeforeTouchingAnUnauthorizedChannel()
    {
        await using var db = CreateContext();
        db.Chatchannels.Add(new Chatchannel
        {
            Channelid = 10,
            Parentid = "parent",
            Tutorid = "tutor",
            Status = ChatChannelStatus.Active
        });
        await db.SaveChangesAsync();

        var hub = new ChatHub(
            NullLogger<ChatHub>.Instance,
            new ServiceCollection().BuildServiceProvider(),
            new ChatRepository(db))
        {
            Context = new TestHubCallerContext("outsider")
        };

        var operations = new Func<Task>[]
        {
            () => hub.JoinChannel(10),
            () => hub.LeaveChannel(10),
            () => hub.Typing(10),
            () => hub.StopTyping(10),
            () => hub.SendMessage(10, "should never be sent")
        };

        foreach (var operation in operations)
        {
            var exception = await Assert.ThrowsAsync<HubException>(operation);
            Assert.Equal("Không thể truy cập kênh trò chuyện.", exception.Message);
        }

        var missingChannel = await Assert.ThrowsAsync<HubException>(() => hub.JoinChannel(999));
        Assert.Equal("Không thể truy cập kênh trò chuyện.", missingChannel.Message);
    }

    [Fact]
    public async Task PresenceEndpoint_UsesTheSameNotFoundForNonPartnerAndMissingTarget()
    {
        await using var db = CreateContext();
        db.Chatchannels.Add(new Chatchannel
        {
            Channelid = 20,
            Parentid = "parent",
            Tutorid = "closed-tutor",
            Status = ChatChannelStatus.Closed
        });
        await db.SaveChangesAsync();

        var presence = new FakePresenceService();
        var controller = CreateController(db, presence, "parent");

        var closedResult = Assert.IsType<NotFoundObjectResult>(
            await controller.GetPresence("closed-tutor"));
        var missingResult = Assert.IsType<NotFoundObjectResult>(
            await controller.GetPresence("missing-user"));

        var closedBody = Assert.IsType<APIResponse<object>>(closedResult.Value);
        var missingBody = Assert.IsType<APIResponse<object>>(missingResult.Value);
        Assert.Equal(closedBody.StatusCode, missingBody.StatusCode);
        Assert.Equal(closedBody.Message, missingBody.Message);
        Assert.Equal(0, presence.SingleReadCount);
        Assert.Contains("no-store", controller.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task PresenceEndpoint_ReturnsServiceUnavailableForUnknownAuthorizedPresence()
    {
        await using var db = CreateContext();
        db.Chatchannels.Add(new Chatchannel
        {
            Channelid = 30,
            Parentid = "parent",
            Tutorid = "tutor",
            Status = ChatChannelStatus.Active
        });
        await db.SaveChangesAsync();

        var presence = new FakePresenceService { ReturnUnknown = true };
        var controller = CreateController(db, presence, "parent");

        var result = Assert.IsType<ObjectResult>(await controller.GetPresence("tutor"));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
        Assert.Equal("5", controller.Response.Headers.RetryAfter.ToString());
        Assert.Equal(1, presence.SingleReadCount);
    }

    [Fact]
    public async Task PresenceBatch_DeduplicatesAndFiltersToSelfAndActivePartners()
    {
        await using var db = CreateContext();
        db.Chatchannels.AddRange(
            new Chatchannel
            {
                Channelid = 40,
                Parentid = "parent",
                Tutorid = "active-tutor",
                Status = ChatChannelStatus.Active
            },
            new Chatchannel
            {
                Channelid = 41,
                Parentid = "parent",
                Tutorid = "closed-tutor",
                Status = ChatChannelStatus.Closed
            });
        await db.SaveChangesAsync();

        var presence = new FakePresenceService();
        var controller = CreateController(db, presence, "parent");

        var result = Assert.IsType<OkObjectResult>(await controller.GetPresenceBatch(
            new PresenceBatchRequest
            {
                UserIds =
                [
                    "parent",
                    "active-tutor",
                    "closed-tutor",
                    "outsider",
                    "active-tutor"
                ]
            }));
        var body = Assert.IsType<APIResponse<IReadOnlyList<UserPresenceResponse>>>(result.Value);

        Assert.Equal(["parent", "active-tutor"], presence.LastBatchUserIds);
        Assert.Equal(["parent", "active-tutor"], body.Content!.Select(item => item.UserId));
        Assert.Contains("no-store", controller.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task PresenceBatch_RejectsMoreThanFiftyIdsBeforeReadingPresence()
    {
        await using var db = CreateContext();
        var presence = new FakePresenceService();
        var controller = CreateController(db, presence, "parent");

        var result = Assert.IsType<BadRequestObjectResult>(
            await controller.GetPresenceBatch(new PresenceBatchRequest
            {
                UserIds = Enumerable.Range(1, 51).Select(index => $"user-{index}").ToList()
            }));

        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        Assert.Empty(presence.LastBatchUserIds);
    }

    private static AgoraDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AgoraDbContext>()
            .UseInMemoryDatabase($"chat-security-{Guid.NewGuid()}")
            .Options;
        return new ChatTestDbContext(options);
    }

    private static ChatController CreateController(
        AgoraDbContext db,
        IPresenceService presence,
        string userId)
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId)],
            authenticationType: "test");
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity)
        };

        return new ChatController(
            null!,
            null!,
            presence,
            new ChatRepository(db),
            NullLogger<ChatController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    private sealed class FakePresenceService : IPresenceService
    {
        public bool ReturnUnknown { get; init; }
        public int SingleReadCount { get; private set; }
        public IReadOnlyList<string> LastBatchUserIds { get; private set; } = [];

        public Task RegisterConnectionAsync(string userId, string connectionId)
            => Task.CompletedTask;

        public Task RefreshConnectionAsync(string userId, string connectionId)
            => Task.CompletedTask;

        public Task RemoveConnectionAsync(string userId, string connectionId)
            => Task.CompletedTask;

        public Task<UserPresenceResponse> GetPresenceAsync(string userId)
        {
            SingleReadCount++;
            return Task.FromResult(CreatePresence(userId));
        }

        public Task<IReadOnlyList<UserPresenceResponse>> GetPresencesAsync(
            IReadOnlyCollection<string> userIds,
            bool includeLastSeen = false)
        {
            LastBatchUserIds = userIds.ToArray();
            return Task.FromResult<IReadOnlyList<UserPresenceResponse>>(
                userIds.Select(CreatePresence).ToArray());
        }

        public Task<int> CleanupExpiredLeasesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        private UserPresenceResponse CreatePresence(string userId)
            => ReturnUnknown
                ? new UserPresenceResponse
                {
                    UserId = userId,
                    IsOnline = null,
                    Status = UserPresenceStatus.Unknown
                }
                : new UserPresenceResponse
                {
                    UserId = userId,
                    IsOnline = true,
                    Status = UserPresenceStatus.Online,
                    Version = 1
                };
    }

    private sealed class ChatTestDbContext(
        DbContextOptions<AgoraDbContext> options) : AgoraDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<QuestionBank>().Ignore(question => question.Embedding);
        }
    }

    private sealed class TestHubCallerContext(string userId) : HubCallerContext
    {
        private readonly ClaimsPrincipal _user = new(
            new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId)],
                authenticationType: "test"));
        private readonly Dictionary<object, object?> _items = [];

        public override string ConnectionId { get; } = Guid.NewGuid().ToString();
        public override string? UserIdentifier => userId;
        public override ClaimsPrincipal User => _user;
        public override IDictionary<object, object?> Items => _items;
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort()
        {
        }
    }
}
