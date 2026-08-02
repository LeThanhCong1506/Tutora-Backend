using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MV.ApplicationLayer.Services;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer;
using MV.InfrastructureLayer.Repositories;
using Xunit;

namespace MV.ApplicationLayer.Tests;

// Maps to Excel sheet "ResendPhoneOtpAsync" (Code_17, SimpleAuthService.ResendPhoneOtpAsync).
public class ResendPhoneOtpAsyncTests
{
    private const string Phone = "0901111111";

    [Fact]
    public async Task BlankPhone_ReturnsRequiredError()
    {
        var service = CreateService(TestSupport.CreateInMemoryContext("resend-otp"));
        var result = await service.ResendPhoneOtpAsync(new ResendPhoneOtpRequest { Phone = "" });

        Assert.Equal("Số điện thoại là bắt buộc.", result.ErrorMessage);
    }

    [Fact]
    public async Task UnknownPhone_ReturnsUserNotFoundError()
    {
        var service = CreateService(TestSupport.CreateInMemoryContext("resend-otp"));
        var result = await service.ResendPhoneOtpAsync(new ResendPhoneOtpRequest { Phone = Phone });

        Assert.Equal("Không tìm thấy người dùng.", result.ErrorMessage);
    }

    [Fact]
    public async Task AlreadyVerifiedAccount_ReturnsAlreadyVerifiedError()
    {
        var db = TestSupport.CreateInMemoryContext("resend-otp");
        db.Users.Add(NewUser(isVerified: true));
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.ResendPhoneOtpAsync(new ResendPhoneOtpRequest { Phone = Phone });

        Assert.Equal("Số điện thoại đã được xác thực.", result.ErrorMessage);
    }

    [Fact]
    public async Task UnverifiedAccount_ResendsOtpSuccessfully()
    {
        var db = TestSupport.CreateInMemoryContext("resend-otp");
        db.Users.Add(NewUser(isVerified: false));
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.ResendPhoneOtpAsync(new ResendPhoneOtpRequest { Phone = Phone });

        Assert.True(string.IsNullOrEmpty(result.ErrorMessage));
        Assert.True(result.RequiresPhoneVerification);
    }

    private static User NewUser(bool isVerified) => new()
    {
        Userid = "user-1",
        Phone = Phone,
        Password = "hash",
        Fullname = "Nguyễn Văn A",
        Isphoneverified = isVerified,
        Primaryrole = UserRole.Student,
        Status = 1,
        Createdat = DateTime.UtcNow
    };

    private static SimpleAuthService CreateService(MV.InfrastructureLayer.DBContext.AgoraDbContext db) =>
        new(
            new UnitOfWork(db, new PasswordRepository(), NullLogger<UnitOfWork>.Instance),
            new PasswordRepository(),
            null!,
            new FakeOtpSender(),
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
            NullLogger<SimpleAuthService>.Instance,
            new FakeDistributedCache(),
            null!);
}
