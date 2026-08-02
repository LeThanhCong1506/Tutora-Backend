using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MV.ApplicationLayer.Services;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using MV.InfrastructureLayer.DBContext;
using MV.InfrastructureLayer.Repositories;
using Xunit;

namespace MV.ApplicationLayer.Tests;

// Maps to Excel sheet "SendChatMessageAsync" (Code_38, ChatService.SendMessageAsync).
public class SendChatMessageAsyncTests
{
    [Fact]
    public async Task UnknownChannel_ThrowsChannelNotFoundException()
    {
        var ctx = CreateService();

        await Assert.ThrowsAsync<ChannelNotFoundException>(
            () => ctx.Service.SendMessageAsync("parent-1", 999, new ChatMessageCreateRequest { Content = "Chào bạn" }));
    }

    [Fact]
    public async Task NonParticipant_ThrowsInvalidMessageException()
    {
        var ctx = CreateService();
        ctx.Db.Chatchannels.Add(NewChannel(1, ChatChannelStatus.Active));
        await ctx.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidMessageException>(
            () => ctx.Service.SendMessageAsync("outsider-1", 1, new ChatMessageCreateRequest { Content = "Chào bạn" }));
    }

    [Fact]
    public async Task ClosedChannel_ThrowsInvalidMessageException()
    {
        var ctx = CreateService();
        ctx.Db.Chatchannels.Add(NewChannel(2, ChatChannelStatus.Closed));
        await ctx.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidMessageException>(
            () => ctx.Service.SendMessageAsync("parent-1", 2, new ChatMessageCreateRequest { Content = "Chào bạn" }));
    }

    [Fact]
    public async Task ParticipantSendsToActiveChannel_PersistsMessageAndUpdatesChannel()
    {
        var ctx = CreateService();
        ctx.Db.Chatchannels.Add(NewChannel(3, ChatChannelStatus.Active));
        await ctx.Db.SaveChangesAsync();

        var response = await ctx.Service.SendMessageAsync("parent-1", 3, new ChatMessageCreateRequest { Content = "Buổi học hôm nay thế nào?" });

        Assert.Equal("Buổi học hôm nay thế nào?", response.Content);
        Assert.Equal("parent-1", response.SenderId);
        var channel = await ctx.Db.Chatchannels.AsNoTracking().SingleAsync(c => c.Channelid == 3);
        Assert.NotNull(channel.Lastmessageat);
        Assert.Single(ctx.Db.Chatmessages.Where(m => m.Channelid == 3));
    }

    private static Chatchannel NewChannel(int id, string status) => new()
    {
        Channelid = id,
        Parentid = "parent-1",
        Tutorid = "tutor-1",
        Status = status,
        Createdat = DateTime.UtcNow
    };

    private static ServiceContext CreateService()
    {
        var db = TestSupport.CreateInMemoryContext("send-chat-message");
        var service = new ChatService(
            new ChatRepository(db),
            new UserRepository(db, new PasswordRepository()),
            new BookingRepository(db),
            null!,
            new FakeNotificationService(),
            NullLogger<ChatService>.Instance);
        return new ServiceContext(service, db);
    }

    private sealed record ServiceContext(ChatService Service, AgoraDbContext Db);
}
