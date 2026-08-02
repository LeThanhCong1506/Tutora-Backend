using Microsoft.Extensions.Logging.Abstractions;
using MV.ApplicationLayer.Services;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer;
using MV.InfrastructureLayer.DBContext;
using MV.InfrastructureLayer.Repositories;
using Xunit;

namespace MV.ApplicationLayer.Tests;

// Maps to Excel sheet "VerifyEkycAsync" (Code_32, EkycService.VerifyAndApplyAsync).
public class VerifyEkycAsyncTests
{
    [Fact]
    public async Task MissingFrontImage_ThrowsArgumentException()
    {
        var ctx = CreateService();
        var user = NewUser();
        var request = new UploadCccdRequest { FrontImage = null!, BackImage = TestSupport.FakeFormFile("back.jpg") };

        await Assert.ThrowsAsync<ArgumentException>(
            () => ctx.Service.VerifyAndApplyAsync(user, request, new EkycVerificationOptions { RequireOcr = true }));
    }

    [Fact]
    public async Task DisallowedImageExtension_ThrowsArgumentException()
    {
        var ctx = CreateService();
        var user = NewUser();
        var request = new UploadCccdRequest { FrontImage = TestSupport.FakeFormFile("front.pdf"), BackImage = TestSupport.FakeFormFile("back.jpg") };

        await Assert.ThrowsAsync<ArgumentException>(
            () => ctx.Service.VerifyAndApplyAsync(user, request, new EkycVerificationOptions { RequireOcr = true }));
    }

    [Fact]
    public async Task OversizedImage_ThrowsArgumentException()
    {
        var ctx = CreateService();
        var user = NewUser();
        var request = new UploadCccdRequest { FrontImage = TestSupport.FakeFormFile("front.jpg", sizeBytes: 6 * 1024 * 1024), BackImage = TestSupport.FakeFormFile("back.jpg") };

        await Assert.ThrowsAsync<ArgumentException>(
            () => ctx.Service.VerifyAndApplyAsync(user, request, new EkycVerificationOptions { RequireOcr = true }));
    }

    [Fact]
    public async Task LowOcrConfidence_ThrowsInvalidOperationException()
    {
        var ctx = CreateService();
        ctx.FptAi.IdCardResponseToReturn = OcrResponse(idProb: "50", name: "Nguyen Van A", id: "001199001234");
        var user = NewUser();
        var request = ValidRequest();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ctx.Service.VerifyAndApplyAsync(user, request, new EkycVerificationOptions { RequireOcr = true }));
    }

    [Fact]
    public async Task NameDoesNotMatchProfile_ThrowsInvalidOperationException()
    {
        var ctx = CreateService();
        ctx.FptAi.IdCardResponseToReturn = OcrResponse(idProb: "95", name: "Hoàn Toàn Khác Tên", id: "001199001234");
        var user = NewUser(fullName: "Nguyễn Văn A");
        var request = ValidRequest();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ctx.Service.VerifyAndApplyAsync(user, request, new EkycVerificationOptions { RequireOcr = true }));
    }

    [Fact]
    public async Task IdNumberAlreadyUsedByAnotherUser_ThrowsInvalidOperationException()
    {
        var ctx = CreateService();
        ctx.Db.Users.Add(new User { Userid = "other-user", Password = "hash", Fullname = "Người khác", Status = 1, Createdat = DateTime.UtcNow, Identitynumber = "enc:001199001234" });
        await ctx.Db.SaveChangesAsync();
        ctx.FptAi.IdCardResponseToReturn = OcrResponse(idProb: "95", name: "Nguyễn Văn A", id: "001199001234");
        var user = NewUser(fullName: "Nguyễn Văn A");
        var request = ValidRequest();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ctx.Service.VerifyAndApplyAsync(user, request, new EkycVerificationOptions { RequireOcr = true }));
    }

    [Fact]
    public async Task ValidCccd_MarksVerifiedAndStoresOnlyEncryptedData()
    {
        var ctx = CreateService();
        ctx.FptAi.IdCardResponseToReturn = OcrResponse(idProb: "95", name: "Nguyễn Văn A", id: "001199001234", dob: "01/01/2000");
        var user = NewUser(fullName: "Nguyễn Văn A");
        var request = ValidRequest();

        var result = await ctx.Service.VerifyAndApplyAsync(user, request, new EkycVerificationOptions { RequireOcr = true, AutoFillProfile = true });

        Assert.True(result.Verified);
        Assert.True(user.Isidentityverified);
        Assert.Null(user.Idcardfronturl);
        Assert.Null(user.Idcardbackurl);
        Assert.Equal("enc:001199001234", user.Identitynumber);
    }

    private static UploadCccdRequest ValidRequest() => new()
    {
        FrontImage = TestSupport.FakeFormFile("front.jpg"),
        BackImage = TestSupport.FakeFormFile("back.jpg")
    };

    private static User NewUser(string fullName = "Nguyễn Văn A") => new()
    {
        Userid = "user-1",
        Password = "hash",
        Fullname = fullName,
        Status = 1,
        Createdat = DateTime.UtcNow
    };

    private static FptAiIdCardResponse OcrResponse(string idProb, string name, string id, string? dob = null) => new()
    {
        Data = new List<FptAiResult>
        {
            new() { Id = id, Name = name, Dob = dob ?? "01/01/2000", Sex = "Nam", Address = "Hà Nội", IdProb = idProb }
        }
    };

    private static ServiceContext CreateService()
    {
        var db = TestSupport.CreateInMemoryContext("verify-ekyc");
        var unitOfWork = new UnitOfWork(db, new PasswordRepository(), NullLogger<UnitOfWork>.Instance);
        var fptAi = new FakeFptAiService();
        var service = new EkycService(fptAi, new FakeEncryptionService(), unitOfWork, NullLogger<EkycService>.Instance);
        return new ServiceContext(service, db, fptAi);
    }

    private sealed record ServiceContext(EkycService Service, AgoraDbContext Db, FakeFptAiService FptAi);
}
