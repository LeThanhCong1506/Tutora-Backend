using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MV.ApplicationLayer.Services;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer;
using MV.InfrastructureLayer.DBContext;
using MV.InfrastructureLayer.Repositories;
using Xunit;

namespace MV.ApplicationLayer.Tests;

// Maps to Excel sheet "AddCertificateAsync" (Code_33, TutorService.AddCertificateAsync).
public class AddCertificateAsyncTests
{
    private const string TutorId = "tutor-1";

    [Fact]
    public async Task MissingTutorProfile_ThrowsArgumentException()
    {
        var (service, _) = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(() => service.AddCertificateAsync(TutorId, ValidRequest()));
    }

    [Fact]
    public async Task DisallowedFileExtension_ThrowsArgumentException()
    {
        var (service, db) = CreateService();
        db.Tutorprofiles.Add(new Tutorprofile { Tutorid = TutorId, Createdat = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var request = ValidRequest();
        request.CertificateFile = TestSupport.FakeFormFile("cert.exe");

        await Assert.ThrowsAsync<ArgumentException>(() => service.AddCertificateAsync(TutorId, request));
    }

    [Fact]
    public async Task OversizedFile_ThrowsArgumentException()
    {
        var (service, db) = CreateService();
        db.Tutorprofiles.Add(new Tutorprofile { Tutorid = TutorId, Createdat = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var request = ValidRequest();
        request.CertificateFile = TestSupport.FakeFormFile("cert.pdf", sizeBytes: 11 * 1024 * 1024);

        await Assert.ThrowsAsync<ArgumentException>(() => service.AddCertificateAsync(TutorId, request));
    }

    [Fact]
    public async Task YearIssuedOutOfRange_ThrowsArgumentException()
    {
        var (service, db) = CreateService();
        db.Tutorprofiles.Add(new Tutorprofile { Tutorid = TutorId, Createdat = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var request = ValidRequest();
        request.YearIssued = 1899;

        await Assert.ThrowsAsync<ArgumentException>(() => service.AddCertificateAsync(TutorId, request));
    }

    [Fact]
    public async Task YearIssuedAboveCurrentYear_ThrowsArgumentException()
    {
        var (service, db) = CreateService();
        db.Tutorprofiles.Add(new Tutorprofile { Tutorid = TutorId, Createdat = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var request = ValidRequest();
        request.YearIssued = DateTime.UtcNow.Year + 1;

        await Assert.ThrowsAsync<ArgumentException>(() => service.AddCertificateAsync(TutorId, request));
    }

    [Fact]
    public async Task ValidCertificate_AlwaysCreatedAsPendingReview()
    {
        var (service, db) = CreateService();
        db.Tutorprofiles.Add(new Tutorprofile { Tutorid = TutorId, Createdat = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var response = await service.AddCertificateAsync(TutorId, ValidRequest());

        Assert.Equal(MV.DomainLayer.Constants.CertificateStatus.PendingReview, response.Certificate.VerificationStatus);
        var stored = await db.Tutorcertificates.AsNoTracking().SingleAsync(c => c.Tutorid == TutorId);
        Assert.Equal(MV.DomainLayer.Constants.CertificateStatus.PendingReview, stored.Verificationstatus);
    }

    private static AddCertificateRequest ValidRequest() => new()
    {
        CertificateName = "IELTS 8.0",
        CertificateType = "language",
        IssuingOrganization = "British Council",
        YearIssued = 2023,
        CertificateFile = TestSupport.FakeFormFile("cert.pdf")
    };

    private static (TutorService Service, AgoraDbContext Db) CreateService()
    {
        var db = TestSupport.CreateInMemoryContext("add-certificate");
        var unitOfWork = new UnitOfWork(db, new PasswordRepository(), NullLogger<UnitOfWork>.Instance);
        var service = new TutorService(
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
        return (service, db);
    }
}
