using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using MV.ApplicationLayer.Services;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer;
using MV.InfrastructureLayer.DBContext;
using MV.InfrastructureLayer.Repositories;
using Xunit;

namespace MV.ApplicationLayer.Tests;

// Maps to Excel sheet "VerifyPhoneOtpAsync" (Code_5, SimpleAuthService.VerifyPhoneOtpAsync).
public class VerifyPhoneOtpAsyncTests
{
    private const string Phone = "0901111111";
    private static string OtpKey(string phone) => $"otp:phone:{phone}";

    [Fact]
    public async Task BlankPhoneOrOtp_ReturnsRequiredError()
    {
        var ctx = CreateService();
        var result = await ctx.Service.VerifyPhoneOtpAsync(new VerifyPhoneOtpRequest { Phone = "", Otp = "123456" });

        Assert.Equal("Số điện thoại và OTP là bắt buộc.", result.ErrorMessage);
    }

    [Fact]
    public async Task UserNotFound_ReturnsError()
    {
        var ctx = CreateService();
        var result = await ctx.Service.VerifyPhoneOtpAsync(new VerifyPhoneOtpRequest { Phone = Phone, Otp = "123456" });

        Assert.Equal("Không tìm thấy người dùng.", result.ErrorMessage);
    }

    [Fact]
    public async Task NoOtpEntry_ReturnsExpiredError()
    {
        var ctx = CreateService();
        ctx.Db.Users.Add(NewUser(isVerified: false));
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();

        var result = await ctx.Service.VerifyPhoneOtpAsync(new VerifyPhoneOtpRequest { Phone = Phone, Otp = "123456" });

        Assert.Equal("OTP đã hết hạn. Vui lòng gửi lại mã mới.", result.ErrorMessage);
    }

    [Fact]
    public async Task AttemptsAtLimit_ReturnsTooManyAttemptsError()
    {
        var ctx = CreateService();
        ctx.Db.Users.Add(NewUser(isVerified: false));
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();
        await SeedOtpAsync(ctx.Cache, "123456", attempts: 5);

        var result = await ctx.Service.VerifyPhoneOtpAsync(new VerifyPhoneOtpRequest { Phone = Phone, Otp = "123456" });

        Assert.Equal("Quá nhiều lần nhập OTP không hợp lệ. Vui lòng gửi lại mã mới.", result.ErrorMessage);
    }

    [Fact]
    public async Task WrongOtpCode_ReturnsInvalidCodeError()
    {
        var ctx = CreateService();
        ctx.Db.Users.Add(NewUser(isVerified: false));
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();
        await SeedOtpAsync(ctx.Cache, "123456", attempts: 0);

        var result = await ctx.Service.VerifyPhoneOtpAsync(new VerifyPhoneOtpRequest { Phone = Phone, Otp = "999999" });

        Assert.Equal("Mã OTP không hợp lệ.", result.ErrorMessage);
    }

    [Fact]
    public async Task CorrectOtp_ActivatesAccountAndReturnsTokens()
    {
        var ctx = CreateService();
        ctx.Db.Users.Add(NewUser(isVerified: false));
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();
        await SeedOtpAsync(ctx.Cache, "123456", attempts: 0);

        var result = await ctx.Service.VerifyPhoneOtpAsync(new VerifyPhoneOtpRequest { Phone = Phone, Otp = "123456" });

        Assert.True(string.IsNullOrEmpty(result.ErrorMessage));
        Assert.NotNull(result.AccessToken);
        Assert.NotNull(result.RefreshToken);
        var updated = await ctx.Db.Users.AsNoTracking().SingleAsync(u => u.Phone == Phone);
        Assert.True(updated.Isphoneverified);
    }

    [Fact]
    public async Task AlreadyVerifiedUser_GetsTokenDirectlyWithoutCheckingOtp()
    {
        var ctx = CreateService();
        ctx.Db.Users.Add(NewUser(isVerified: true));
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();

        var result = await ctx.Service.VerifyPhoneOtpAsync(new VerifyPhoneOtpRequest { Phone = Phone, Otp = "anything" });

        Assert.True(string.IsNullOrEmpty(result.ErrorMessage));
        Assert.NotNull(result.AccessToken);
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

    private static async Task SeedOtpAsync(IDistributedCache cache, string code, int attempts)
    {
        var json = JsonSerializer.Serialize(new { Code = code, Attempts = attempts, ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5) });
        await cache.SetStringAsync(OtpKey(Phone), json, new DistributedCacheEntryOptions());
    }

    private static ServiceContext CreateService()
    {
        var db = TestSupport.CreateInMemoryContext("verify-phone-otp");
        var unitOfWork = new UnitOfWork(db, new PasswordRepository(), NullLogger<UnitOfWork>.Instance);
        var cache = new FakeDistributedCache();
        var service = new SimpleAuthService(
            unitOfWork,
            new PasswordRepository(),
            new FakeAuthenticationRepository(),
            new FakeOtpSender(),
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
            NullLogger<SimpleAuthService>.Instance,
            cache,
            null!);
        return new ServiceContext(service, db, cache);
    }

    private sealed record ServiceContext(SimpleAuthService Service, AgoraDbContext Db, IDistributedCache Cache);
}
