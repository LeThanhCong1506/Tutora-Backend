using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MV.ApplicationLayer.Helpers;
using MV.ApplicationLayer.Services;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer.DBContext;
using MV.InfrastructureLayer.Repositories;
using Xunit;

namespace MV.ApplicationLayer.Tests;

/// <summary>
/// Mục G (Link 3), phần logic thuần: DisputeRelearnPolicy quyết định (a) request hợp lệ hay không
/// khi outcome=Reschedule, (b) buổi học lại mới được dựng như thế nào từ buổi gốc. Tách khỏi
/// DisputeService.CloseDisputeAsync vì method đó dùng FromSqlRaw để lock row (đặc thù Postgres) —
/// EF InMemory provider không dịch được FromSqlRaw, nên KHÔNG thể test toàn bộ CloseDisputeAsync
/// (đúng thực trạng: DisputeService không có test nào trong repo vì lý do này, không riêng gì đây).
/// </summary>
public class DisputeRelearnPolicyTests
{
    private static readonly DateTime Now = new(2026, 8, 20, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ValidateRelearnRequest_CompletedOutcome_NeverThrows_RegardlessOfRelearnScheduledStart()
    {
        var request = new CloseDisputeRequest { ClassSessionOutcome = CloseDisputeOutcomes.Completed, Note = "x", RelearnScheduledStart = null };

        DisputeRelearnPolicy.ValidateRelearnRequest(request, Now); // không throw
    }

    [Fact]
    public void ValidateRelearnRequest_RescheduleOutcome_MissingRelearnScheduledStart_Throws()
    {
        var request = new CloseDisputeRequest { ClassSessionOutcome = CloseDisputeOutcomes.Reschedule, Note = "x", RelearnScheduledStart = null };

        var ex = Assert.Throws<ArgumentException>(() => DisputeRelearnPolicy.ValidateRelearnRequest(request, Now));
        Assert.Contains("giờ học lại", ex.Message);
    }

    [Fact]
    public void ValidateRelearnRequest_RescheduleOutcome_RelearnScheduledStartInThePast_Throws()
    {
        var request = new CloseDisputeRequest
        {
            ClassSessionOutcome = CloseDisputeOutcomes.Reschedule,
            Note = "x",
            RelearnScheduledStart = Now.AddHours(-1)
        };

        var ex = Assert.Throws<ArgumentException>(() => DisputeRelearnPolicy.ValidateRelearnRequest(request, Now));
        Assert.Contains("tương lai", ex.Message);
    }

    [Fact]
    public void ValidateRelearnRequest_RescheduleOutcome_RelearnScheduledStartExactlyNow_Throws()
    {
        var request = new CloseDisputeRequest
        {
            ClassSessionOutcome = CloseDisputeOutcomes.Reschedule,
            Note = "x",
            RelearnScheduledStart = Now // <= now, không chấp nhận đúng thời điểm hiện tại
        };

        Assert.Throws<ArgumentException>(() => DisputeRelearnPolicy.ValidateRelearnRequest(request, Now));
    }

    [Fact]
    public void ValidateRelearnRequest_RescheduleOutcome_FutureRelearnScheduledStart_DoesNotThrow()
    {
        var request = new CloseDisputeRequest
        {
            ClassSessionOutcome = CloseDisputeOutcomes.Reschedule,
            Note = "x",
            RelearnScheduledStart = Now.AddDays(1)
        };

        DisputeRelearnPolicy.ValidateRelearnRequest(request, Now); // không throw
    }

    [Fact]
    public void BuildRelearnSession_CopiesBookingTutorStudent_AndFlagsAsDisputeRelearn()
    {
        var original = new ClassSession
        {
            Classsessionid = 10,
            Bookingid = 1,
            Tutorid = "tutor-1",
            Studentid = "student-profile-1",
            Scheduledstart = Now.AddDays(-2),
            Scheduledend = Now.AddDays(-2).AddMinutes(90),
        };
        var relearnStart = Now.AddDays(3);

        var relearn = DisputeRelearnPolicy.BuildRelearnSession(original, relearnStart, Now);

        Assert.True(relearn.Isdisputerelearn);
        Assert.False(relearn.Iscontinuation); // không lẫn với link 2 (buổi phụ do ngắt giữa chừng)
        Assert.Equal(original.Classsessionid, relearn.Originalsessionid);
        Assert.Equal(original.Bookingid, relearn.Bookingid);
        Assert.Equal(original.Tutorid, relearn.Tutorid);
        Assert.Equal(original.Studentid, relearn.Studentid);
        Assert.Equal(0m, relearn.Lessonprice);
        Assert.Equal(ClassSessionStatus.Scheduled, relearn.Status);
        Assert.Equal(relearnStart, relearn.Scheduledstart);
        // Giữ đúng thời lượng buổi gốc (90 phút), không phải thời lượng cố định nào khác.
        Assert.Equal(TimeSpan.FromMinutes(90), relearn.Scheduledend - relearn.Scheduledstart);
        Assert.Equal(Now, relearn.Createdat);
    }

    [Fact]
    public void BuildRelearnSession_ShortOriginalDuration_PreservesExactDuration()
    {
        var original = new ClassSession
        {
            Classsessionid = 20,
            Bookingid = 2,
            Tutorid = "tutor-2",
            Studentid = "student-profile-2",
            Scheduledstart = Now.AddDays(-1),
            Scheduledend = Now.AddDays(-1).AddMinutes(30),
        };

        var relearn = DisputeRelearnPolicy.BuildRelearnSession(original, Now.AddDays(5), Now);

        Assert.Equal(TimeSpan.FromMinutes(30), relearn.Scheduledend - relearn.Scheduledstart);
    }
}

/// <summary>
/// Chuỗi (buổi gốc + mọi buổi bù/phụ do gián đoạn + mọi buổi học lại do hoà giải) đủ
/// MaxRelearnSessionsPerChain (3) buổi thì buổi cuối cùng trong chuỗi là buổi CUỐI CÙNG còn được
/// học lại — nếu chính buổi đó lại bị khiếu nại, CloseDisputeAsync phải chặn tạo thêm buổi, bắt
/// buộc xử lý bằng hoàn tiền. CountSessionsInChainAsync đếm TỔNG số buổi trong chuỗi (không riêng
/// buổi Isdisputerelearn) — bug thực tế từng gặp: nếu chỉ đếm riêng Isdisputerelearn thì 1 buổi bù
/// do gián đoạn (Iscontinuation) xen giữa chuỗi không bị tính, khiến cap không kích hoạt đúng lúc
/// (xem test ChainWithContinuationThenRelearn_CountsBothTypes_AtCap). Hàm này chỉ dùng LINQ thường
/// (không FromSqlRaw) nên test được trực tiếp với EF InMemory, khác phần còn lại của
/// CloseDisputeAsync (xem docstring CloseDisputeAsyncEarlyValidationTests bên dưới).
/// </summary>
public class DisputeRelearnPolicyChainCountTests
{
    private static readonly DateTime Base = new(2026, 8, 20, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ChainWithRootAndOneRelearnSession_CountIsTwo_StillBelowCap()
    {
        await using var db = CreateContext();
        Seed(db, id: 100, bookingId: 500, originalSessionId: null, isDisputeRelearn: false, isContinuation: false, start: Base);
        var relearn1 = Seed(db, id: 101, bookingId: 500, originalSessionId: 100, isDisputeRelearn: true, isContinuation: false, start: Base.AddDays(1));
        await db.SaveChangesAsync();

        var count = await DisputeRelearnPolicy.CountSessionsInChainAsync(db, relearn1.Classsessionid);

        Assert.Equal(2, count);
        Assert.True(count < DisputeRelearnPolicy.MaxRelearnSessionsPerChain); // vẫn còn được tạo thêm buổi thứ 3
    }

    [Fact]
    public async Task ChainWithRootAndTwoRelearnSessions_CountIsThree_AtCap()
    {
        await using var db = CreateContext();
        Seed(db, id: 200, bookingId: 600, originalSessionId: null, isDisputeRelearn: false, isContinuation: false, start: Base);
        Seed(db, id: 201, bookingId: 600, originalSessionId: 200, isDisputeRelearn: true, isContinuation: false, start: Base.AddDays(1));
        var relearn2 = Seed(db, id: 202, bookingId: 600, originalSessionId: 201, isDisputeRelearn: true, isContinuation: false, start: Base.AddDays(2));
        await db.SaveChangesAsync();

        var count = await DisputeRelearnPolicy.CountSessionsInChainAsync(db, relearn2.Classsessionid);

        Assert.Equal(3, count);
        // Buổi này (buổi thứ 3 trong chuỗi) chính là "buổi cuối cùng còn được học lại" — nếu NÓ lại
        // bị khiếu nại, count đã bằng cap nên CloseDisputeAsync phải từ chối tạo buổi thứ 4.
        Assert.True(count >= DisputeRelearnPolicy.MaxRelearnSessionsPerChain);
    }

    [Fact]
    public async Task ChainWithContinuationThenRelearn_CountsBothTypes_AtCap()
    {
        // Đúng kịch bản bug thực tế: buổi 1 (gốc) bị gián đoạn -> hệ thống tự tạo buổi 2
        // (Iscontinuation=true, KHÔNG phải Isdisputerelearn) -> buổi 2 bị khiếu nại, Admin hoà giải
        // tạo buổi 3 (Isdisputerelearn=true). Nếu chỉ đếm Isdisputerelearn thì count=1 (chưa chạm
        // cap), cho phép tạo tiếp buổi 4 — sai với yêu cầu "chuỗi đủ 3 buổi thì dừng". Đếm đúng theo
        // TỔNG số buổi trong chuỗi phải ra 3 (buổi 1+2+3), tức đã chạm cap ngay tại buổi 3.
        await using var db = CreateContext();
        Seed(db, id: 300, bookingId: 700, originalSessionId: null, isDisputeRelearn: false, isContinuation: false, start: Base);
        Seed(db, id: 301, bookingId: 700, originalSessionId: 300, isDisputeRelearn: false, isContinuation: true, start: Base.AddDays(1));
        var relearn = Seed(db, id: 302, bookingId: 700, originalSessionId: 301, isDisputeRelearn: true, isContinuation: false, start: Base.AddDays(2));
        await db.SaveChangesAsync();

        var count = await DisputeRelearnPolicy.CountSessionsInChainAsync(db, relearn.Classsessionid);

        Assert.Equal(3, count);
        Assert.True(count >= DisputeRelearnPolicy.MaxRelearnSessionsPerChain); // phải chặn tạo buổi thứ 4
    }

    private static ClassSession Seed(AgoraDbContext db, int id, int bookingId, int? originalSessionId, bool isDisputeRelearn, bool isContinuation, DateTime start)
    {
        var session = new ClassSession
        {
            Classsessionid = id,
            Bookingid = bookingId,
            Originalsessionid = originalSessionId,
            Isdisputerelearn = isDisputeRelearn,
            Iscontinuation = isContinuation,
            Scheduledstart = start,
            Scheduledend = start.AddMinutes(60),
        };
        db.ClassSessions.Add(session);
        return session;
    }

    private static AgoraDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AgoraDbContext>()
            .UseInMemoryDatabase($"dispute-relearn-chain-count-{Guid.NewGuid()}")
            .Options;
        return new ChainCountTestDbContext(options);
    }

    private sealed class ChainCountTestDbContext(DbContextOptions<AgoraDbContext> options)
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

/// <summary>
/// Phần còn lại của CloseDisputeAsync (load booking/dispute/classSession qua FromSqlRaw để lock
/// row) không test được với EF InMemory — 2 test dưới đây chỉ xác nhận validate chạy TRƯỚC khi vào
/// transaction (nên không cần DB thật vẫn throw đúng), khớp đúng những gì code thực sự làm: xem
/// DisputeService.CloseDisputeAsync — DisputeRelearnPolicy.ValidateRelearnRequest được gọi ngay đầu
/// method, trước cả truy vấn snapshot.
/// </summary>
public class CloseDisputeAsyncEarlyValidationTests
{
    [Fact]
    public async Task RescheduleOutcome_MissingRelearnScheduledStart_ThrowsBeforeTouchingDatabase()
    {
        await using var db = CreateEmptyContext();
        var service = CreateService(db);
        var request = new CloseDisputeRequest { ClassSessionOutcome = CloseDisputeOutcomes.Reschedule, Note = "Hai bên đã thống nhất học lại" };

        // disputeId=999 không tồn tại trong DB trống -> nếu code chạm DB trước, sẽ ném
        // ArgumentException("Không tìm thấy tranh chấp") thay vì lỗi validate giờ học lại.
        // Test này xác nhận đúng THỨ TỰ: validate input chạy trước, không cần biết dispute có tồn tại.
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.CloseDisputeAsync(999, "admin-1", request));
        Assert.Contains("giờ học lại", ex.Message);
    }

    [Fact]
    public async Task RescheduleOutcome_RelearnScheduledStartInThePast_ThrowsBeforeTouchingDatabase()
    {
        await using var db = CreateEmptyContext();
        var service = CreateService(db);
        var request = new CloseDisputeRequest
        {
            ClassSessionOutcome = CloseDisputeOutcomes.Reschedule,
            Note = "Hai bên đã thống nhất học lại",
            RelearnScheduledStart = DateTime.UtcNow.AddHours(-1)
        };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.CloseDisputeAsync(999, "admin-1", request));
        Assert.Contains("tương lai", ex.Message);
    }

    private static DisputeService CreateService(AgoraDbContext db)
        => new(
            disputeRepo: new DisputeRepository(db),
            context: db,
            settlementService: null!,
            warningService: null!,
            notificationService: null!,
            storageService: null!,
            hubContext: null!,
            classificationService: null!,
            recordingAccessTokenService: null!,
            backgroundJobClient: null!,
            sessionLogService: null!,
            logger: NullLogger<DisputeService>.Instance);

    private static AgoraDbContext CreateEmptyContext()
    {
        var options = new DbContextOptionsBuilder<AgoraDbContext>()
            .UseInMemoryDatabase($"dispute-close-early-validation-{Guid.NewGuid()}")
            .Options;
        return new EarlyValidationTestDbContext(options);
    }

    private sealed class EarlyValidationTestDbContext(DbContextOptions<AgoraDbContext> options)
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
