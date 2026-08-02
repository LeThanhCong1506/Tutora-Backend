using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer.Repositories;
using Xunit;

namespace MV.ApplicationLayer.Tests;

// Maps to Excel sheet "SearchTutorsAsync" (Code_24, TutorSearchRepository.SearchTutorsAsync).
// SearchTerm and GradeLevel filters use EF.Functions.ILike (Postgres-only) - not exercised
// here since the InMemory provider cannot translate it. Base eligibility + numeric/exact
// filters (no SearchTerm/GradeLevel) run fine on InMemory and are covered below.
public class SearchTutorsAsyncTests
{
    [Fact]
    public async Task OnlyActivePublicAcceptingBookingsTutors_AreReturned()
    {
        var db = TestSupport.CreateInMemoryContext("search-tutors");
        db.Users.Add(NewTutorUser("tutor-active", out var activeProfile));
        db.Tutorprofiles.Add(activeProfile);
        db.Users.Add(NewTutorUser("tutor-inactive", out var inactiveProfile));
        inactiveProfile.Ispublic = false;
        db.Tutorprofiles.Add(inactiveProfile);
        await db.SaveChangesAsync();
        var repo = new TutorSearchRepository(db);

        var result = await repo.SearchTutorsAsync(new TutorSearchParameters { PageNumber = 1, PageSize = 10 });

        var tutor = Assert.Single(result.Items);
        Assert.Equal("tutor-active", tutor.TutorId);
    }

    [Fact]
    public async Task NoMatchingTutors_ReturnsEmptyPage()
    {
        var db = TestSupport.CreateInMemoryContext("search-tutors");
        await db.SaveChangesAsync();
        var repo = new TutorSearchRepository(db);

        var result = await repo.SearchTutorsAsync(new TutorSearchParameters { PageNumber = 1, PageSize = 10 });

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task MinRatingFilter_ExcludesTutorsBelowThreshold()
    {
        var db = TestSupport.CreateInMemoryContext("search-tutors");
        db.Users.Add(NewTutorUser("tutor-high-rated", out var highRated));
        highRated.Averagerating = 4.8;
        db.Tutorprofiles.Add(highRated);
        db.Users.Add(NewTutorUser("tutor-low-rated", out var lowRated));
        lowRated.Averagerating = 3.0;
        db.Tutorprofiles.Add(lowRated);
        await db.SaveChangesAsync();
        var repo = new TutorSearchRepository(db);

        var result = await repo.SearchTutorsAsync(new TutorSearchParameters { PageNumber = 1, PageSize = 10, MinRating = 4.0 });

        var tutor = Assert.Single(result.Items);
        Assert.Equal("tutor-high-rated", tutor.TutorId);
    }

    private static User NewTutorUser(string id, out Tutorprofile profile)
    {
        var user = new User
        {
            Userid = id,
            Password = "hash",
            Fullname = "Gia sư " + id,
            Primaryrole = UserRole.Tutor,
            Status = 1,
            Createdat = DateTime.UtcNow
        };
        profile = new Tutorprofile
        {
            Tutorid = id,
            Profilestatus = TutorProfileStatus.Active,
            Ispublic = true,
            Isacceptingbookings = true,
            Createdat = DateTime.UtcNow
        };
        return user;
    }
}
