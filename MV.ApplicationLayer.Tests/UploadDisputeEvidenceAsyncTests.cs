using Microsoft.Extensions.Logging.Abstractions;
using MV.ApplicationLayer.Services;
using MV.DomainLayer.Constants;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer.DBContext;
using Xunit;

namespace MV.ApplicationLayer.Tests;

// Maps to Excel sheet "UploadDisputeEvidenceAsync" (Code_31, DisputeService.UploadTutorDisputeEvidenceAsync).
// Unlike most Dispute/Settlement functions, this one has no FromSqlRaw/ExecuteUpdateAsync -
// plain LINQ + SaveChangesAsync throughout - so it's fully testable on EF InMemory.
// The only gate is dispute.Status == Pending; settle state and file validity are not checked.
public class UploadDisputeEvidenceAsyncTests
{
    [Fact]
    public async Task PendingDispute_ValidFile_UploadsAndRecordsEvidence()
    {
        var db = TestSupport.CreateInMemoryContext("upload-dispute-evidence");
        db.ClassSessions.Add(new ClassSession { Classsessionid = 1, Tutorid = "tutor-1", Status = ClassSessionStatus.Disputed, Scheduledstart = DateTime.UtcNow.AddDays(-1), Scheduledend = DateTime.UtcNow.AddDays(-1).AddHours(1) });
        db.Disputes.Add(new Dispute { Classsessionid = 1, Status = DisputeStatus.Pending, Disputetype = DisputeTypes.Quality, Reason = "test", Createdat = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var file = TestSupport.FakeFormFile("evidence.jpg");

        var url = await service.UploadTutorDisputeEvidenceAsync(1, "tutor-1", file);

        Assert.False(string.IsNullOrEmpty(url));
        var evidence = Assert.Single(db.DisputeEvidences);
        Assert.Equal("tutor-1", evidence.Uploadedby);
    }

    [Fact]
    public async Task DisputeNotPending_ThrowsInvalidOperationException()
    {
        var db = TestSupport.CreateInMemoryContext("upload-dispute-evidence");
        db.ClassSessions.Add(new ClassSession { Classsessionid = 2, Tutorid = "tutor-1", Status = ClassSessionStatus.Completed, Scheduledstart = DateTime.UtcNow.AddDays(-1), Scheduledend = DateTime.UtcNow.AddDays(-1).AddHours(1) });
        db.Disputes.Add(new Dispute { Classsessionid = 2, Status = DisputeStatus.Resolved, Disputetype = DisputeTypes.Quality, Reason = "test", Createdat = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var file = TestSupport.FakeFormFile("evidence.jpg");

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UploadTutorDisputeEvidenceAsync(2, "tutor-1", file));
    }

    [Fact]
    public async Task NoDisputeForSession_ThrowsArgumentException()
    {
        var db = TestSupport.CreateInMemoryContext("upload-dispute-evidence");
        var service = CreateService(db);
        var file = TestSupport.FakeFormFile("evidence.jpg");

        await Assert.ThrowsAsync<ArgumentException>(() => service.UploadTutorDisputeEvidenceAsync(999, "tutor-1", file));
    }

    [Fact]
    public async Task SettledSessionWithPendingDispute_StillAcceptsEvidence()
    {
        // Settle state is deliberately NOT a gate here: the only condition is that the dispute
        // itself is still Pending, so a tutor can keep filing evidence after the session settled.
        var db = TestSupport.CreateInMemoryContext("upload-dispute-evidence");
        db.ClassSessions.Add(new ClassSession { Classsessionid = 3, Tutorid = "tutor-1", Status = ClassSessionStatus.Completed, Issettled = true, Scheduledstart = DateTime.UtcNow.AddDays(-1), Scheduledend = DateTime.UtcNow.AddDays(-1).AddHours(1) });
        db.Disputes.Add(new Dispute { Classsessionid = 3, Status = DisputeStatus.Pending, Disputetype = DisputeTypes.Quality, Reason = "test", Createdat = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var url = await service.UploadTutorDisputeEvidenceAsync(3, "tutor-1", TestSupport.FakeFormFile("evidence.jpg"));

        Assert.False(string.IsNullOrEmpty(url));
        Assert.Single(db.DisputeEvidences);
    }

    [Fact]
    public async Task NullFile_ThrowsNullReferenceException_NoGuardExists()
    {
        // Documents a real gap: the service never null-checks the file, so a null upload surfaces
        // as an unhandled NullReferenceException instead of a 400. Asserted so the day someone
        // adds the guard, this test fails and the spec gets revisited.
        var db = TestSupport.CreateInMemoryContext("upload-dispute-evidence");
        db.ClassSessions.Add(new ClassSession { Classsessionid = 4, Tutorid = "tutor-1", Status = ClassSessionStatus.Disputed, Scheduledstart = DateTime.UtcNow.AddDays(-1), Scheduledend = DateTime.UtcNow.AddDays(-1).AddHours(1) });
        db.Disputes.Add(new Dispute { Classsessionid = 4, Status = DisputeStatus.Pending, Disputetype = DisputeTypes.Quality, Reason = "test", Createdat = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var service = CreateService(db);

        await Assert.ThrowsAsync<NullReferenceException>(() => service.UploadTutorDisputeEvidenceAsync(4, "tutor-1", null!));
    }

    private static DisputeService CreateService(AgoraDbContext db) => new(
        null!, db, null!, null!, new FakeNotificationService(), new FakeFileStorageService(),
        null!, null!, null!, NullLogger<DisputeService>.Instance);
}
