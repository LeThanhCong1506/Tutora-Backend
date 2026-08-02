using Microsoft.Extensions.Logging.Abstractions;
using MV.ApplicationLayer.Services;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer;
using MV.InfrastructureLayer.DBContext;
using MV.InfrastructureLayer.Repositories;
using Xunit;

namespace MV.ApplicationLayer.Tests;

// Maps to Excel sheet "AddAvailabilityBulkAsync" (Code_19, TutorAvailabilityService.BulkAddAvailabilitiesAsync).
public class AddAvailabilityBulkAsyncTests
{
    private const string TutorId = "tutor-1";

    [Fact]
    public async Task StartAfterEnd_ThrowsArgumentException()
    {
        var ctx = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(() => ctx.Service.BulkAddAvailabilitiesAsync(TutorId, new BulkCreateAvailabilityRequest
        {
            Availabilities = new List<CreateAvailabilityRequest>
            {
                new() { Dayofweek = 1, Starttime = "10:00", Endtime = "09:00" }
            }
        }));
    }

    [Fact]
    public async Task OverlapsExistingDbSlot_ThrowsInvalidOperationException()
    {
        var ctx = CreateService();
        ctx.Db.Tutoravailabilities.Add(new Tutoravailability { Tutorid = TutorId, Dayofweek = 1, Starttime = new TimeOnly(9, 0), Endtime = new TimeOnly(11, 0), Createdat = DateTime.UtcNow });
        await ctx.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => ctx.Service.BulkAddAvailabilitiesAsync(TutorId, new BulkCreateAvailabilityRequest
        {
            Availabilities = new List<CreateAvailabilityRequest>
            {
                new() { Dayofweek = 1, Starttime = "10:00", Endtime = "12:00" }
            }
        }));
    }

    [Fact]
    public async Task OverlapsWithinSameBatch_ThrowsInvalidOperationException()
    {
        var ctx = CreateService();

        await Assert.ThrowsAsync<InvalidOperationException>(() => ctx.Service.BulkAddAvailabilitiesAsync(TutorId, new BulkCreateAvailabilityRequest
        {
            Availabilities = new List<CreateAvailabilityRequest>
            {
                new() { Dayofweek = 1, Starttime = "09:00", Endtime = "11:00" },
                new() { Dayofweek = 1, Starttime = "10:00", Endtime = "12:00" }
            }
        }));
    }

    [Fact]
    public async Task NonOverlappingValidSlots_CreatesAllSlots()
    {
        var ctx = CreateService();

        var result = await ctx.Service.BulkAddAvailabilitiesAsync(TutorId, new BulkCreateAvailabilityRequest
        {
            Availabilities = new List<CreateAvailabilityRequest>
            {
                new() { Dayofweek = 1, Starttime = "09:00", Endtime = "11:00" },
                new() { Dayofweek = 2, Starttime = "14:00", Endtime = "16:00" }
            }
        });

        Assert.Equal(2, result.Count);
        Assert.Equal(2, ctx.Db.Tutoravailabilities.Count(a => a.Tutorid == TutorId));
    }

    private static ServiceContext CreateService()
    {
        var db = TestSupport.CreateInMemoryContext("add-availability-bulk");
        var unitOfWork = new UnitOfWork(db, new PasswordRepository(), NullLogger<UnitOfWork>.Instance);
        var tutorService = new TutorService(
            unitOfWork,
            new FakeFileStorageService(),
            null!,
            new FakeNotificationService(),
            NullLogger<TutorService>.Instance,
            null!,
            null!,
            db,
            null!,
            new FakeTutorEmbedQueue());
        var service = new TutorAvailabilityService(db, tutorService);
        return new ServiceContext(service, db);
    }

    private sealed record ServiceContext(TutorAvailabilityService Service, AgoraDbContext Db);
}
