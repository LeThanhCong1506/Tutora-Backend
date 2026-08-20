using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MV.ApplicationLayer.Services;
using MV.DomainLayer.Constants;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Helpers;
using MV.InfrastructureLayer.DBContext;
using Xunit;

namespace MV.ApplicationLayer.Tests;

/// <summary>
/// B3: Đề xuất đổi lịch cho buổi phụ (Iscontinuation=true) khác buổi thường ở 2 điểm — bỏ buffer
/// 2h trước giờ học (tình huống khẩn cấp), nhưng bị chặn cứng ở cuối ngày bị ngắt (UTC thuần,
/// không quy đổi múi giờ). Buổi thường (Iscontinuation=false) phải giữ đúng hành vi cũ.
///
/// TimeZoneHelper.UtcNow trả thẳng DateTime.UtcNow, không có seam để giả lập "now" trong test —
/// mọi mốc thời gian dưới đây tính TƯƠNG ĐỐI theo DateTime.UtcNow lúc test chạy, không dùng ngày
/// giờ cố định, để không phụ thuộc vào ngày thực khi CI/máy dev chạy test.
/// </summary>
public class ClassSessionRescheduleProposalContinuationTests
{
    private const string TutorId = "tutor-1";
    private const string StudentUserId = "student-user-1";
    private const int SessionId = 1;

    [Fact]
    public async Task ContinuationSession_CanProposeLessThanTwoHoursAhead()
    {
        await using var db = CreateContext();
        var interruptedAt = DateTime.UtcNow;
        SeedSession(db, isContinuation: true, interruptedAt: interruptedAt,
            scheduledStart: interruptedAt.AddMinutes(-5), scheduledEnd: interruptedAt.AddMinutes(25));
        await db.SaveChangesAsync();
        var service = CreateService(db);

        // Chỉ 10 phút nữa — buổi thường sẽ bị chặn bởi buffer 2h, buổi phụ thì không.
        var proposedStart = DateTime.UtcNow.AddMinutes(10);
        var response = await service.ProposeAsync(SessionId, TutorId, UserRole.Tutor, proposedStart, "kẹt xe");

        Assert.Equal(RescheduleProposalStatus.Pending, response.Status);
        Assert.Equal(proposedStart, response.ProposedScheduledStart);
    }

    [Fact]
    public async Task ContinuationSession_CannotProposeATimeOnTheNextCalendarDay()
    {
        await using var db = CreateContext();
        var now = DateTime.UtcNow;
        var interruptedAt = now;
        SeedSession(db, isContinuation: true, interruptedAt: interruptedAt,
            scheduledStart: now.AddMinutes(-30), scheduledEnd: now.AddMinutes(30));
        await db.SaveChangesAsync();
        var service = CreateService(db);

        // 30 phút sau nửa đêm (đầu ngày mai) -> chắc chắn khác ngày với `now`, và chắc chắn ở
        // tương lai so với `now` (không phụ thuộc giờ thực lúc test chạy).
        var proposedStart = now.Date.AddDays(1).AddMinutes(30);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ProposeAsync(SessionId, TutorId, UserRole.Tutor, proposedStart, null));
        Assert.Contains("cùng ngày", ex.Message);
    }

    [Fact]
    public async Task ContinuationSession_CanProposeATimeLaterTheSameCalendarDay()
    {
        await using var db = CreateContext();
        var now = DateTime.UtcNow;
        var interruptedAt = now;
        SeedSession(db, isContinuation: true, interruptedAt: interruptedAt,
            scheduledStart: now.AddMinutes(-30), scheduledEnd: now.AddMinutes(30));
        await db.SaveChangesAsync();
        var service = CreateService(db);

        // 5 phút nữa cùng ngày.
        var proposedStart = now.AddMinutes(5);

        var response = await service.ProposeAsync(SessionId, TutorId, UserRole.Tutor, proposedStart, null);

        Assert.Equal(RescheduleProposalStatus.Pending, response.Status);
        // Hạn phản hồi phải bị chặn ở cuối ngày bị ngắt (interruptedAt.Date + 1 ngày), không phải
        // 24h sau `now` — vì cuối ngày luôn sớm hơn 24h kể từ bây giờ (trừ khi ngắt đúng lúc 00:00).
        Assert.Equal(interruptedAt.Date.AddDays(1), response.ExpiresAt);
    }

    [Fact]
    public async Task NonContinuationSession_StillRequiresTwoHourBuffer()
    {
        await using var db = CreateContext();
        var scheduledStart = DateTime.UtcNow.AddMinutes(30); // dưới 2 tiếng
        SeedSession(db, isContinuation: false, interruptedAt: null,
            scheduledStart: scheduledStart, scheduledEnd: scheduledStart.AddMinutes(60));
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ProposeAsync(SessionId, TutorId, UserRole.Tutor, DateTime.UtcNow.AddHours(5), null));
        Assert.Contains("2 giờ", ex.Message);
    }

    private static void SeedSession(
        AgoraDbContext db, bool isContinuation, DateTime? interruptedAt, DateTime scheduledStart, DateTime scheduledEnd)
    {
        var tutorUser = new User { Userid = TutorId, Username = TutorId, Password = "x", Email = "tutor@test.local", Fullname = "Gia sư", Primaryrole = UserRole.Tutor };
        var studentUser = new User { Userid = StudentUserId, Username = StudentUserId, Password = "x", Email = "student@test.local", Fullname = "Học sinh", Primaryrole = UserRole.Student, Birthdate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-20)) };
        var tutor = new Tutorprofile { Tutorid = TutorId, Tutor = tutorUser };
        var student = new Studentprofile
        {
            Studentid = "student-profile-1",
            Linkeduserid = StudentUserId,
            Linkeduser = studentUser,
            Fullname = studentUser.Fullname,
        };
        var booking = new Booking { Bookingid = 1, Studentid = student.Studentid, Student = student, Tutorid = TutorId, Tutor = tutor };
        var session = new ClassSession
        {
            Classsessionid = SessionId,
            Bookingid = booking.Bookingid,
            Booking = booking,
            Tutorid = TutorId,
            Tutor = tutor,
            Studentid = student.Studentid,
            Student = student,
            Scheduledstart = scheduledStart,
            Scheduledend = scheduledEnd,
            Status = ClassSessionStatus.Scheduled,
            Iscontinuation = isContinuation,
            Interruptedat = interruptedAt,
        };
        db.Users.AddRange(tutorUser, studentUser);
        db.Tutorprofiles.Add(tutor);
        db.Studentprofiles.Add(student);
        db.Bookings.Add(booking);
        db.ClassSessions.Add(session);
    }

    private static ClassSessionRescheduleProposalService CreateService(AgoraDbContext db)
        => new(db, null!, null!, NullLogger<ClassSessionRescheduleProposalService>.Instance);

    private static AgoraDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AgoraDbContext>()
            .UseInMemoryDatabase($"reschedule-continuation-{Guid.NewGuid()}")
            .Options;
        return new RescheduleContinuationTestDbContext(options);
    }

    private sealed class RescheduleContinuationTestDbContext(DbContextOptions<AgoraDbContext> options)
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
