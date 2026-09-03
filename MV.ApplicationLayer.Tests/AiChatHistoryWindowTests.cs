using Microsoft.EntityFrameworkCore;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer.DBContext;
using MV.InfrastructureLayer.Repositories;
using Xunit;

namespace MV.ApplicationLayer.Tests;

/// <summary>
/// History đưa cho AI phải là phần ĐUÔI của hội thoại. Bản cũ gọi
/// GetMessagesPagedAsync(sessionId, 1, 20) — sắp xếp tăng dần nên trang 1 là 20 tin CŨ NHẤT:
/// user đã đăng nhập chat quá 10 lượt thì AI chỉ đọc mãi phần mở đầu, không thấy gì vừa nói.
/// </summary>
public class AiChatHistoryWindowTests
{
    [Fact]
    public async Task GetRecentMessages_lay_phan_duoi_va_dung_thu_tu_hoi_thoai()
    {
        await using var ctx = CreateContext();
        var sessionId = Guid.NewGuid();
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // 30 tin: "msg-0" (cũ nhất) → "msg-29" (mới nhất).
        for (var i = 0; i < 30; i++)
            ctx.ChatHistories.Add(new ChatHistory
            {
                MessageId = Guid.NewGuid(),
                SessionId = sessionId,
                Role = i % 2 == 0 ? "user" : "assistant",
                Content = $"msg-{i}",
                CreatedAt = start.AddMinutes(i)
            });
        await ctx.SaveChangesAsync();

        var repo = new AiChatRepository(ctx);
        var recent = await repo.GetRecentMessagesAsync(sessionId, 20);

        Assert.Equal(20, recent.Count);

        // Phải là 20 tin MỚI NHẤT (msg-10..msg-29), không phải msg-0..msg-19.
        Assert.Equal("msg-10", recent[0].Content);
        Assert.Equal("msg-29", recent[^1].Content);

        // Và theo thứ tự hội thoại (cũ → mới) — đảo ngược thì AI đọc hội thoại ngược đời.
        Assert.Equal(
            Enumerable.Range(10, 20).Select(i => $"msg-{i}").ToArray(),
            recent.Select(m => m.Content).ToArray());
    }

    [Fact]
    public async Task Hoi_thoai_ngan_hon_cua_so_thi_lay_het()
    {
        await using var ctx = CreateContext();
        var sessionId = Guid.NewGuid();
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        for (var i = 0; i < 3; i++)
            ctx.ChatHistories.Add(new ChatHistory
            {
                MessageId = Guid.NewGuid(),
                SessionId = sessionId,
                Role = "user",
                Content = $"msg-{i}",
                CreatedAt = start.AddMinutes(i)
            });
        await ctx.SaveChangesAsync();

        var recent = await new AiChatRepository(ctx).GetRecentMessagesAsync(sessionId, 20);

        Assert.Equal(new[] { "msg-0", "msg-1", "msg-2" }, recent.Select(m => m.Content).ToArray());
    }

    [Fact]
    public async Task Khong_lay_nham_tin_cua_phien_khac()
    {
        await using var ctx = CreateContext();
        var mine = Guid.NewGuid();
        var other = Guid.NewGuid();
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        ctx.ChatHistories.Add(new ChatHistory
        {
            MessageId = Guid.NewGuid(), SessionId = mine, Role = "user",
            Content = "cua toi", CreatedAt = start
        });
        // Tin của phiên khác, mới hơn → nếu lọc sai sẽ chen lên đầu kết quả.
        ctx.ChatHistories.Add(new ChatHistory
        {
            MessageId = Guid.NewGuid(), SessionId = other, Role = "user",
            Content = "cua nguoi khac", CreatedAt = start.AddMinutes(5)
        });
        await ctx.SaveChangesAsync();

        var recent = await new AiChatRepository(ctx).GetRecentMessagesAsync(mine, 20);

        Assert.Equal(new[] { "cua toi" }, recent.Select(m => m.Content).ToArray());
    }

    private static AgoraDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AgoraDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new HistoryTestDbContext(options);
    }

    private sealed class HistoryTestDbContext(DbContextOptions<AgoraDbContext> options)
        : AgoraDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<QuestionBank>().Ignore(question => question.Embedding);
            modelBuilder.Entity<TutoraKbChunk>().Ignore(chunk => chunk.Embedding);
        }
    }
}
