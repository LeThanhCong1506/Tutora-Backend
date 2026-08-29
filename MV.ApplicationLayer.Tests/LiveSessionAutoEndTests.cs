using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.InMemory.Diagnostics.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.ApplicationLayer.Services;
using MV.DomainLayer.Constants;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer.DBContext;
using Xunit;

namespace MV.ApplicationLayer.Tests;

/// <summary>
/// AutoCloseExpiredLiveSessionsAsync đóng phòng khi 1 buổi InProgress đã quá
/// Scheduledend + LiveSessionAutoEndGraceMinutes mà chưa ai bấm "Kết thúc". Buổi PHỤ
/// (Iscontinuation) là ngoại lệ: Scheduledend của nó chỉ là mốc ước tính lúc TẠO (now+1h), không
/// phải giờ hẹn thật — hai bên có thể check-in trễ hàng giờ so với mốc này. Nếu vẫn dùng
/// Scheduledend để tính hạn, buổi phụ vừa check-in xong sẽ bị job này đóng ngay ở lượt chạy kế
/// tiếp vì Scheduledend đã nằm trong quá khứ từ trước khi ai kịp vào học — đúng bug user report
/// "vào được 2 giây là bị văng ra hết" khi cả 2 vừa check-in.
/// </summary>
public class LiveSessionAutoEndTests
{
    private static readonly int GraceMinutes = ClassSessionService.LiveSessionAutoEndGraceMinutes;

    [Fact]
    public async Task NonContinuation_ClosesWhenScheduledEndPastGrace()
    {
        var db = CreateContext();
        var now = DateTime.UtcNow;
        db.ClassSessions.Add(new ClassSession
        {
            Classsessionid = 1,
            Status = ClassSessionStatus.InProgress,
            Iscontinuation = false,
            Scheduledstart = now.AddHours(-2),
            Scheduledend = now.AddMinutes(-(GraceMinutes + 5)),
            Checkintime = now.AddHours(-2),
        });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var closed = await service.AutoCloseExpiredLiveSessionsAsync();

        Assert.Equal(1, closed);
        var session = await db.ClassSessions.FirstAsync(l => l.Classsessionid == 1);
        Assert.NotNull(session.Checkouttime);
    }

    [Fact]
    public async Task NonContinuation_DoesNotCloseWithinGrace()
    {
        var db = CreateContext();
        var now = DateTime.UtcNow;
        db.ClassSessions.Add(new ClassSession
        {
            Classsessionid = 2,
            Status = ClassSessionStatus.InProgress,
            Iscontinuation = false,
            Scheduledstart = now.AddHours(-1),
            Scheduledend = now.AddMinutes(-5),
            Checkintime = now.AddHours(-1),
        });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var closed = await service.AutoCloseExpiredLiveSessionsAsync();

        Assert.Equal(0, closed);
        var session = await db.ClassSessions.FirstAsync(l => l.Classsessionid == 2);
        Assert.Null(session.Checkouttime);
    }

    [Fact]
    public async Task Continuation_DoesNotCloseJustBecauseScheduledEndIsStale_WhenJustCheckedIn()
    {
        var db = CreateContext();
        var now = DateTime.UtcNow;
        // Buổi phụ được tạo lúc "now-3h" với Scheduledend = now+1h theo mốc ước tính khi đó, nên
        // Scheduledend thực tế = (now-3h)+1h = now-2h — đã nằm sâu trong quá khứ. Nhưng người
        // dùng vừa mới check-in THẬT sự cách đây 30s — vẫn phải cho học tiếp bình thường.
        db.ClassSessions.Add(new ClassSession
        {
            Classsessionid = 3,
            Status = ClassSessionStatus.InProgress,
            Iscontinuation = true,
            Scheduledstart = now.AddHours(-3),
            Scheduledend = now.AddHours(-2),
            Checkintime = now.AddSeconds(-30),
        });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var closed = await service.AutoCloseExpiredLiveSessionsAsync();

        Assert.Equal(0, closed);
        var session = await db.ClassSessions.FirstAsync(l => l.Classsessionid == 3);
        Assert.Null(session.Checkouttime);
    }

    [Fact]
    public async Task Continuation_ClosesWhenActuallyOverdueSinceCheckIn()
    {
        var db = CreateContext();
        var now = DateTime.UtcNow;
        // Thời lượng tính sẵn (Scheduledend-Scheduledstart) là 30 phút; check-in đã 1 tiếng trước
        // → quá cả thời lượng lẫn grace 30p tính từ CHECK-IN THẬT, phải bị đóng.
        db.ClassSessions.Add(new ClassSession
        {
            Classsessionid = 4,
            Status = ClassSessionStatus.InProgress,
            Iscontinuation = true,
            Scheduledstart = now.AddHours(-5),
            Scheduledend = now.AddHours(-5).AddMinutes(30),
            Checkintime = now.AddHours(-1),
        });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var closed = await service.AutoCloseExpiredLiveSessionsAsync();

        Assert.Equal(1, closed);
        var session = await db.ClassSessions.FirstAsync(l => l.Classsessionid == 4);
        Assert.NotNull(session.Checkouttime);
    }

    private sealed class DisabledCloudRecordingService : ICloudRecordingService
    {
        public bool Enabled => false;
        public Task<CloudRecordingHandle> StartAsync(int classSessionId, string channel, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<CloudRecordingResult> StopAsync(int classSessionId, string channel, string resourceId, string sid, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private static ClassSessionService CreateService(AgoraDbContext db) =>
        new ClassSessionService(
            classSessionRepo: null!,
            bookingRepo: null!,
            studentRepo: null!,
            context: db,
            chatService: null!,
            notificationService: null!,
            zaloOAService: null!,
            storageService: null!,
            presence: null!,
            cloudRecording: new DisabledCloudRecordingService(),
            settlementService: null!,
            warningService: null!,
            recordingAccessTokenService: null!,
            backgroundJobClient: null!,
            rescheduleProposalService: null!,
            sessionLogService: null!,
            logger: NullLogger<ClassSessionService>.Instance);

    private static AgoraDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AgoraDbContext>()
            .UseInMemoryDatabase($"live-session-auto-end-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new LiveSessionAutoEndTestDbContext(options);
    }

    private sealed class LiveSessionAutoEndTestDbContext(DbContextOptions<AgoraDbContext> options)
        : AgoraDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<QuestionBank>().Ignore(x => x.Embedding);
            modelBuilder.Entity<TutoraKbChunk>().Ignore(x => x.Embedding);
        }
    }
}
