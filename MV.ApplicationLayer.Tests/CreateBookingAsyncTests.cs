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

// Maps to Excel sheet "CreateBookingAsync" (Code_9, BookingService.CreateBookingAsync).
public class CreateBookingAsyncTests
{
    private const string ParentId = "parent-1";
    private const string StudentId = "student-profile-1";
    private const string TutorId = "tutor-1";

    [Fact]
    public async Task StartDateInThePast_ThrowsBookingException()
    {
        var ctx = CreateService();
        var request = new CreateBookingRequest { TutorId = TutorId, StudentId = StudentId, TutorSubjectGradePriceId = 1, PackageId = 1, StartDate = DateTime.UtcNow.AddDays(-1) };

        var ex = await Assert.ThrowsAsync<BookingException>(() => ctx.Service.CreateBookingAsync(ParentId, UserRole.Parent, request));
        Assert.Equal(BookingErrorCodes.InvalidStartDate, ex.ErrorCode);
    }

    [Fact]
    public async Task TutorNotFound_ThrowsBookingException()
    {
        var ctx = CreateService();
        await SeedStudentAsync(ctx.Db);
        var request = new CreateBookingRequest { TutorId = "no-such-tutor", StudentId = StudentId, TutorSubjectGradePriceId = 1, PackageId = 1, StartDate = NextMonday() };

        var ex = await Assert.ThrowsAsync<BookingException>(() => ctx.Service.CreateBookingAsync(ParentId, UserRole.Parent, request));
        Assert.Equal(BookingErrorCodes.TutorNotFound, ex.ErrorCode);
    }

    [Fact]
    public async Task TutorNotAcceptingBookings_ThrowsBookingException()
    {
        var ctx = CreateService();
        await SeedStudentAsync(ctx.Db);
        SeedActiveTutor(ctx.Db, acceptingBookings: false);
        await ctx.Db.SaveChangesAsync();
        var request = new CreateBookingRequest { TutorId = TutorId, StudentId = StudentId, TutorSubjectGradePriceId = 1, PackageId = 1, StartDate = NextMonday() };

        var ex = await Assert.ThrowsAsync<BookingException>(() => ctx.Service.CreateBookingAsync(ParentId, UserRole.Parent, request));
        Assert.Equal(BookingErrorCodes.TutorNotAvailable, ex.ErrorCode);
    }

    [Fact]
    public async Task FlexiblePackageWithThreeOrFewerSlots_ThrowsBookingException()
    {
        var ctx = CreateService();
        await SeedStudentAsync(ctx.Db);
        SeedActiveTutor(ctx.Db, acceptingBookings: true);
        SeedPriceAndPackage(ctx.Db);
        await ctx.Db.SaveChangesAsync();
        var monday = NextMonday();
        var request = new CreateBookingRequest
        {
            TutorId = TutorId,
            StudentId = StudentId,
            TutorSubjectGradePriceId = 1,
            PackageId = 1,
            StartDate = monday,
            FlexibleSlots = new List<FlexibleBookingSlotRequest>
            {
                new() { ScheduledStart = monday.AddHours(9), ScheduledEnd = monday.AddHours(10) }
            }
        };

        var ex = await Assert.ThrowsAsync<BookingException>(() => ctx.Service.CreateBookingAsync(ParentId, UserRole.Parent, request));
        Assert.Equal(BookingErrorCodes.InvalidSchedule, ex.ErrorCode);
    }

    [Fact]
    public async Task ValidFlexibleBooking_CreatesBookingWithClassSessions()
    {
        var ctx = CreateService();
        await SeedStudentAsync(ctx.Db);
        SeedActiveTutor(ctx.Db, acceptingBookings: true);
        SeedPriceAndPackage(ctx.Db);
        ctx.Db.Tutoravailabilities.Add(new Tutoravailability { Tutorid = TutorId, Dayofweek = 1, Starttime = new TimeOnly(9, 0), Endtime = new TimeOnly(10, 0), Createdat = DateTime.UtcNow });
        await ctx.Db.SaveChangesAsync();

        var firstMonday = NextMonday();
        var slots = Enumerable.Range(0, 4)
            .Select(i => firstMonday.AddDays(7 * i))
            .Select(day => new FlexibleBookingSlotRequest { ScheduledStart = day.AddHours(9), ScheduledEnd = day.AddHours(10) })
            .ToList();

        var request = new CreateBookingRequest
        {
            TutorId = TutorId,
            StudentId = StudentId,
            TutorSubjectGradePriceId = 1,
            PackageId = 1,
            StartDate = firstMonday,
            TotalSessions = 4,
            FlexibleSlots = slots
        };

        var result = await ctx.Service.CreateBookingAsync(ParentId, UserRole.Parent, request);

        Assert.Equal(BookingStatus.PendingPayment, result.Status);
        var stored = await ctx.Db.Bookings.Include(b => b.ClassSessions).AsNoTracking().SingleAsync(b => b.Tutorid == TutorId);
        Assert.Equal(4, stored.ClassSessions.Count);
        Assert.Equal(4, stored.Totalsessions);
    }

    [Fact]
    public async Task SelfBookingStudentNotIdentityVerified_ThrowsBookingException()
    {
        var ctx = CreateService();
        // Self-booking student: no Parentid, so the identity/age gate applies.
        ctx.Db.Users.Add(new User { Userid = "student-user-1", Password = "hash", Fullname = "Học sinh tự đặt", Primaryrole = UserRole.Student, Status = 1, Isidentityverified = false, Createdat = DateTime.UtcNow });
        ctx.Db.Studentprofiles.Add(new Studentprofile { Studentid = "student-profile-2", Linkeduserid = "student-user-1", Fullname = "Học sinh tự đặt", Createdat = DateTime.UtcNow });
        await ctx.Db.SaveChangesAsync();
        var request = new CreateBookingRequest { TutorId = TutorId, StudentId = "student-profile-2", TutorSubjectGradePriceId = 1, PackageId = 1, StartDate = NextMonday() };

        var ex = await Assert.ThrowsAsync<BookingException>(() => ctx.Service.CreateBookingAsync("student-user-1", UserRole.Student, request));
        Assert.Equal(BookingErrorCodes.StudentIdentityNotVerified, ex.ErrorCode);
    }

    [Fact]
    public async Task SlotOutsideTutorAvailability_ThrowsBookingException()
    {
        var ctx = CreateService();
        await SeedStudentAsync(ctx.Db);
        SeedActiveTutor(ctx.Db, acceptingBookings: true);
        SeedPriceAndPackage(ctx.Db);
        // Tutor is only available 09:00-10:00 on Mondays; the request asks for 14:00-15:00.
        ctx.Db.Tutoravailabilities.Add(new Tutoravailability { Tutorid = TutorId, Dayofweek = 1, Starttime = new TimeOnly(9, 0), Endtime = new TimeOnly(10, 0), Createdat = DateTime.UtcNow });
        await ctx.Db.SaveChangesAsync();

        var firstMonday = NextMonday();
        var slots = Enumerable.Range(0, 4)
            .Select(i => firstMonday.AddDays(7 * i))
            .Select(day => new FlexibleBookingSlotRequest { ScheduledStart = day.AddHours(14), ScheduledEnd = day.AddHours(15) })
            .ToList();
        var request = new CreateBookingRequest
        {
            TutorId = TutorId,
            StudentId = StudentId,
            TutorSubjectGradePriceId = 1,
            PackageId = 1,
            StartDate = firstMonday,
            TotalSessions = 4,
            FlexibleSlots = slots
        };

        var ex = await Assert.ThrowsAsync<BookingException>(() => ctx.Service.CreateBookingAsync(ParentId, UserRole.Parent, request));
        Assert.Equal(BookingErrorCodes.ScheduleNotInAvailability, ex.ErrorCode);
    }

    private static DateTime NextMonday()
    {
        var day = DateTime.UtcNow.Date.AddDays(1);
        while (day.DayOfWeek != DayOfWeek.Monday) day = day.AddDays(1);
        return DateTime.SpecifyKind(day, DateTimeKind.Utc);
    }

    private static async Task SeedStudentAsync(AgoraDbContext db)
    {
        db.Users.Add(new User { Userid = ParentId, Password = "hash", Fullname = "Phụ huynh", Primaryrole = UserRole.Parent, Status = 1, Createdat = DateTime.UtcNow });
        db.Studentprofiles.Add(new Studentprofile { Studentid = StudentId, Parentid = ParentId, Fullname = "Học sinh", Createdat = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    private static void SeedActiveTutor(AgoraDbContext db, bool acceptingBookings)
    {
        db.Users.Add(new User { Userid = TutorId, Password = "hash", Fullname = "Gia sư", Primaryrole = UserRole.Tutor, Status = 1, Createdat = DateTime.UtcNow });
        db.Tutorprofiles.Add(new Tutorprofile
        {
            Tutorid = TutorId,
            Profilestatus = TutorProfileStatus.Active,
            Ispublic = true,
            Isacceptingbookings = acceptingBookings,
            Createdat = DateTime.UtcNow
        });
    }

    private static void SeedPriceAndPackage(AgoraDbContext db)
    {
        db.Subjects.Add(new Subject { Subjectid = 1, Subjectname = "Toán", IsActive = true });
        db.Gradelevels.Add(new Gradelevel { Gradelevelid = 1, Gradename = "Lớp 10", IsActive = true });
        db.Tutorsubjectgradeprices.Add(new Tutorsubjectgradeprice
        {
            Id = 1,
            Tutorid = TutorId,
            Subjectid = 1,
            Gradelevelid = 1,
            Priceperhour = 100_000m,
            Durationminutespersession = 60,
            Currency = "VND",
            Isactive = true
        });
        db.Tutorpackages.Add(new Tutorpackage
        {
            Packageid = 1,
            Tutorid = TutorId,
            Name = "Gói linh hoạt",
            Packagetype = Tutorpackage.FlexiblePackageType,
            Isactive = true,
            Createdat = DateTime.UtcNow
        });
    }

    private static ServiceContext CreateService()
    {
        var db = TestSupport.CreateInMemoryContext("create-booking");
        var service = new BookingService(
            new BookingRepository(db),
            new StudentRepository(db),
            new TutorRepository(db),
            db,
            new FakeNotificationService(),
            null!,
            null!,
            null!,
            NullLogger<BookingService>.Instance);
        return new ServiceContext(service, db);
    }

    private sealed record ServiceContext(BookingService Service, AgoraDbContext Db);
}
