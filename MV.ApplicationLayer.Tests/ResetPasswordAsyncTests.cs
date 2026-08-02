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

// Maps to Excel sheet "ResetPasswordAsync" (Code_8, SimpleAuthService.ResetPasswordAsync).
public class ResetPasswordAsyncTests
{
    private const string Phone = "0901111111";
    private static string OtpKey(string phone) => $"otp:pwdreset:{phone}";

    [Fact]
    public async Task MissingFields_ReturnsRequiredError()
    {
        var ctx = CreateService();
        var result = await ctx.Service.ResetPasswordAsync(new ResetPasswordRequest { Phone = "", Otp = "123456", NewPassword = "NewPass1" });

        Assert.Equal("Số điện thoại, OTP và mật khẩu mới là bắt buộc.", result.ErrorMessage);
    }

    [Fact]
    public async Task UnknownPhone_ReturnsInvalidRequestError()
    {
        var ctx = CreateService();
        var result = await ctx.Service.ResetPasswordAsync(new ResetPasswordRequest { Phone = Phone, Otp = "123456", NewPassword = "NewPass1" });

        Assert.Equal("Yêu cầu không hợp lệ.", result.ErrorMessage);
    }

    [Fact]
    public async Task NoOtpEntry_ReturnsExpiredError()
    {
        var ctx = CreateService();
        ctx.Db.Users.Add(NewUser());
        await ctx.Db.SaveChangesAsync();

        var result = await ctx.Service.ResetPasswordAsync(new ResetPasswordRequest { Phone = Phone, Otp = "123456", NewPassword = "NewPass1" });

        Assert.Equal("OTP đã hết hạn. Vui lòng yêu cầu lại.", result.ErrorMessage);
    }

    [Fact]
    public async Task AttemptsAtLimit_ReturnsTooManyAttemptsError()
    {
        var ctx = CreateService();
        ctx.Db.Users.Add(NewUser());
        await ctx.Db.SaveChangesAsync();
        await SeedOtpAsync(ctx.Cache, "123456", attempts: 5);

        var result = await ctx.Service.ResetPasswordAsync(new ResetPasswordRequest { Phone = Phone, Otp = "123456", NewPassword = "NewPass1" });

        Assert.Equal("Quá nhiều lần nhập OTP không hợp lệ. Vui lòng yêu cầu lại.", result.ErrorMessage);
    }

    [Fact]
    public async Task WrongOtp_ReturnsInvalidCodeError()
    {
        var ctx = CreateService();
        ctx.Db.Users.Add(NewUser());
        await ctx.Db.SaveChangesAsync();
        await SeedOtpAsync(ctx.Cache, "123456", attempts: 0);

        var result = await ctx.Service.ResetPasswordAsync(new ResetPasswordRequest { Phone = Phone, Otp = "000000", NewPassword = "NewPass1" });

        Assert.Equal("Mã OTP không hợp lệ.", result.ErrorMessage);
    }

    [Fact]
    public async Task CorrectOtp_ResetsPasswordSuccessfully()
    {
        var ctx = CreateService();
        ctx.Db.Users.Add(NewUser());
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();
        await SeedOtpAsync(ctx.Cache, "123456", attempts: 0);

        var result = await ctx.Service.ResetPasswordAsync(new ResetPasswordRequest { Phone = Phone, Otp = "123456", NewPassword = "NewPass1" });

        Assert.True(string.IsNullOrEmpty(result.ErrorMessage));
        var updated = await ctx.Db.Users.AsNoTracking().SingleAsync(u => u.Phone == Phone);
        Assert.True(new PasswordRepository().VerifyPassword("NewPass1", updated.Password));
    }

    private static User NewUser() => new()
    {
        Userid = "user-1",
        Phone = Phone,
        Password = new PasswordRepository().HashPassword("OldPass1"),
        Fullname = "Nguyễn Văn A",
        Isphoneverified = true,
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
        var db = TestSupport.CreateInMemoryContext("reset-password");
        var unitOfWork = new UnitOfWork(db, new PasswordRepository(), NullLogger<UnitOfWork>.Instance);
        var cache = new FakeDistributedCache();
        var service = new SimpleAuthService(
            unitOfWork,
            new PasswordRepository(),
            null!,
            new FakeOtpSender(),
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
            NullLogger<SimpleAuthService>.Instance,
            cache,
            null!);
        return new ServiceContext(service, db, cache);
    }

    private sealed record ServiceContext(SimpleAuthService Service, AgoraDbContext Db, IDistributedCache Cache);
}
