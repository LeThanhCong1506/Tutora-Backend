using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MV.ApplicationLayer.Services;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer.DBContext;
using Xunit;

namespace MV.ApplicationLayer.Tests;

// Maps to Excel sheet "CreateFeedbackAsync" (Code_21, FeedbackService.CreateFeedbackAsync).
public class CreateFeedbackAsyncTests
{
    private const string ParentId = "parent-1";
    private const string StudentId = "student-1";
    private const string TutorId = "tutor-1";

    [Fact]
    public async Task SessionNotFoundOrNotOwned_ThrowsArgumentException()
    {
        var db = TestSupport.CreateInMemoryContext("create-feedback");
        var service = new FeedbackService(db, NullLogger<FeedbackService>.Instance);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.CreateFeedbackAsync(ParentId, new CreateFeedbackRequest { ClassSessionId = 999, Rating = 5 }));
    }

    [Fact]
    public async Task SessionNotCompleted_ThrowsInvalidOperationException()
    {
        var db = TestSupport.CreateInMemoryContext("create-feedback");
        SeedCompletedSession(db, sessionId: 1, sessionStatus: ClassSessionStatus.Scheduled);
        await db.SaveChangesAsync();
        var service = new FeedbackService(db, NullLogger<FeedbackService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateFeedbackAsync(ParentId, new CreateFeedbackRequest { ClassSessionId = 1, Rating = 5 }));
    }

    [Fact]
    public async Task DuplicateFeedback_ThrowsInvalidOperationException()
    {
        var db = TestSupport.CreateInMemoryContext("create-feedback");
        SeedCompletedSession(db, sessionId: 2, sessionStatus: ClassSessionStatus.Completed);
        db.Feedbacks.Add(new Feedback { Classsessionid = 2, Fromuserid = ParentId, Touserid = TutorId, Rating = 4, Feedbacktype = FeedbackType.ParentToTutor, Isvisible = true, Createdat = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var service = new FeedbackService(db, NullLogger<FeedbackService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateFeedbackAsync(ParentId, new CreateFeedbackRequest { ClassSessionId = 2, Rating = 5 }));
    }

    [Fact]
    public async Task ValidFeedback_CreatesFeedbackAndRecalculatesTutorRating()
    {
        var db = TestSupport.CreateInMemoryContext("create-feedback");
        SeedCompletedSession(db, sessionId: 3, sessionStatus: ClassSessionStatus.Completed);
        db.Tutorprofiles.Add(new Tutorprofile { Tutorid = TutorId, Createdat = DateTime.UtcNow, Averagerating = 0, Totalreviews = 0 });
        await db.SaveChangesAsync();
        var service = new FeedbackService(db, NullLogger<FeedbackService>.Instance);

        var result = await service.CreateFeedbackAsync(ParentId, new CreateFeedbackRequest { ClassSessionId = 3, Rating = 5, Comment = "Rất tốt" });

        Assert.Equal(5, result.Rating);
        var tutorProfile = await db.Tutorprofiles.AsNoTracking().SingleAsync(t => t.Tutorid == TutorId);
        Assert.Equal(5.0, tutorProfile.Averagerating);
        Assert.Equal(1, tutorProfile.Totalreviews);
    }

    private static void SeedCompletedSession(AgoraDbContext db, int sessionId, string sessionStatus)
    {
        db.Studentprofiles.Add(new Studentprofile { Studentid = StudentId, Parentid = ParentId, Fullname = "Học sinh", Createdat = DateTime.UtcNow });
        db.Bookings.Add(new Booking { Bookingid = sessionId, Studentid = StudentId, Parentid = ParentId, Tutorid = TutorId, Status = BookingStatus.Completed });
        db.ClassSessions.Add(new ClassSession
        {
            Classsessionid = sessionId,
            Bookingid = sessionId,
            Studentid = StudentId,
            Tutorid = TutorId,
            Status = sessionStatus,
            Scheduledstart = DateTime.UtcNow.AddDays(-1),
            Scheduledend = DateTime.UtcNow.AddDays(-1).AddHours(1)
        });
    }
}
