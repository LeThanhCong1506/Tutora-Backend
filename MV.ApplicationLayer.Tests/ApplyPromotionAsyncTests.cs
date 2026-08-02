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

// Maps to Excel sheet "ApplyPromotionAsync" (Code_10, BookingService.ResolvePromotionAsync,
// via the public ValidatePromotionAsync it delegates to).
public class ApplyPromotionAsyncTests
{
    private const string ParentId = "parent-1";
    private const string StudentId = "student-profile-1";
    private const string TutorId = "tutor-1";

    [Fact]
    public async Task ValidActivePercentCode_ReturnsCappedDiscount()
    {
        var ctx = CreateService();
        ctx.Db.Promotions.Add(new Promotion { Promotionid = 1, Code = "SALE10", Isactive = true, Discounttype = DiscountType.Percent, Discountvalue = 10, Maxdiscountamount = 20_000, Minordervalue = 200_000 });
        await ctx.Db.SaveChangesAsync();

        var result = await ctx.Service.ValidatePromotionAsync("SALE10", 1_000_000m);

        Assert.True(result.Valid);
        Assert.Equal(200_000m, result.MinOrderValue);
    }

    [Fact]
    public async Task UnknownCode_ReturnsInvalid()
    {
        var ctx = CreateService();

        var result = await ctx.Service.ValidatePromotionAsync("NOPE", 1_000_000m);

        Assert.False(result.Valid);
    }

    [Fact]
    public async Task ExpiredCode_ReturnsInvalid()
    {
        var ctx = CreateService();
        ctx.Db.Promotions.Add(new Promotion { Promotionid = 2, Code = "OLD10", Isactive = true, Enddate = DateTime.UtcNow.AddDays(-1) });
        await ctx.Db.SaveChangesAsync();

        var result = await ctx.Service.ValidatePromotionAsync("OLD10", 1_000_000m);

        Assert.False(result.Valid);
    }

    [Fact]
    public async Task UsageLimitReached_ReturnsInvalid()
    {
        var ctx = CreateService();
        ctx.Db.Promotions.Add(new Promotion { Promotionid = 3, Code = "MAXED", Isactive = true, Usagelimit = 5, Usagecount = 5 });
        await ctx.Db.SaveChangesAsync();

        var result = await ctx.Service.ValidatePromotionAsync("MAXED", 1_000_000m);

        Assert.False(result.Valid);
    }

    [Fact]
    public async Task BelowMinimumOrder_ReturnsInvalidWithMinOrderValue()
    {
        var ctx = CreateService();
        ctx.Db.Promotions.Add(new Promotion { Promotionid = 4, Code = "SALE10", Isactive = true, Minordervalue = 200_000 });
        await ctx.Db.SaveChangesAsync();

        var result = await ctx.Service.ValidatePromotionAsync("SALE10", 100_000m);

        Assert.False(result.Valid);
        Assert.Equal(200_000m, result.MinOrderValue);
    }

    [Fact]
    public async Task BelowMinimumOrder_AppliedDuringBooking_ThrowsPromotionInvalid_NotPromotionMinOrder()
    {
        // Documents a real bug: ResolvePromotionAsync only routes to PromotionMinOrder when
        // validate.Message contains the English word "minimum", but the actual message is the
        // Vietnamese "chưa đạt giá trị tối thiểu" - so this branch is unreachable in practice and
        // every invalid code (including below-minimum-order) surfaces as PromotionInvalid.
        var ctx = CreateService();
        SeedBookingPrerequisites(ctx.Db);
        ctx.Db.Promotions.Add(new Promotion { Promotionid = 5, Code = "SALE10", Isactive = true, Minordervalue = 999_999_999m });
        ctx.Db.Tutoravailabilities.Add(new Tutoravailability { Tutorid = TutorId, Dayofweek = 1, Starttime = new TimeOnly(9, 0), Endtime = new TimeOnly(10, 0), Createdat = DateTime.UtcNow });
        await ctx.Db.SaveChangesAsync();

        var firstMonday = NextMonday();
        var request = new CreateBookingRequest
        {
            TutorId = TutorId,
            StudentId = StudentId,
            TutorSubjectGradePriceId = 1,
            PackageId = 1,
            StartDate = firstMonday,
            TotalSessions = 4,
            PromotionCode = "SALE10",
            FlexibleSlots = Enumerable.Range(0, 4)
                .Select(i => firstMonday.AddDays(7 * i))
                .Select(day => new FlexibleBookingSlotRequest { ScheduledStart = day.AddHours(9), ScheduledEnd = day.AddHours(10) })
                .ToList()
        };

        var ex = await Assert.ThrowsAsync<BookingException>(() => ctx.Service.CreateBookingAsync(ParentId, UserRole.Parent, request));

        Assert.Equal(BookingErrorCodes.PromotionInvalid, ex.ErrorCode);
    }

    private static DateTime NextMonday()
    {
        var day = DateTime.UtcNow.Date.AddDays(1);
        while (day.DayOfWeek != DayOfWeek.Monday) day = day.AddDays(1);
        return DateTime.SpecifyKind(day, DateTimeKind.Utc);
    }

    private static void SeedBookingPrerequisites(AgoraDbContext db)
    {
        db.Users.Add(new User { Userid = ParentId, Password = "hash", Fullname = "Phụ huynh", Primaryrole = UserRole.Parent, Status = 1, Createdat = DateTime.UtcNow });
        db.Studentprofiles.Add(new Studentprofile { Studentid = StudentId, Parentid = ParentId, Fullname = "Học sinh", Createdat = DateTime.UtcNow });
        db.Users.Add(new User { Userid = TutorId, Password = "hash", Fullname = "Gia sư", Primaryrole = UserRole.Tutor, Status = 1, Createdat = DateTime.UtcNow });
        db.Tutorprofiles.Add(new Tutorprofile { Tutorid = TutorId, Profilestatus = TutorProfileStatus.Active, Ispublic = true, Isacceptingbookings = true, Createdat = DateTime.UtcNow });
        db.Subjects.Add(new Subject { Subjectid = 1, Subjectname = "Toán", IsActive = true });
        db.Gradelevels.Add(new Gradelevel { Gradelevelid = 1, Gradename = "Lớp 10", IsActive = true });
        db.Tutorsubjectgradeprices.Add(new Tutorsubjectgradeprice { Id = 1, Tutorid = TutorId, Subjectid = 1, Gradelevelid = 1, Priceperhour = 100_000m, Durationminutespersession = 60, Currency = "VND", Isactive = true });
        db.Tutorpackages.Add(new Tutorpackage { Packageid = 1, Tutorid = TutorId, Name = "Gói linh hoạt", Packagetype = Tutorpackage.FlexiblePackageType, Isactive = true, Createdat = DateTime.UtcNow });
    }

    private static ServiceContext CreateService()
    {
        var db = TestSupport.CreateInMemoryContext("apply-promotion");
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
