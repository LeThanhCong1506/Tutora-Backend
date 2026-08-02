using Microsoft.EntityFrameworkCore;
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

// Maps to Excel sheet "LoginAsync" (Code_6, SimpleAuthService.SimpleLoginAsync).
public class LoginAsyncTests
{
    private static readonly PasswordRepository Passwords = new();

    [Fact]
    public async Task BlankCredentials_ReturnsRequiredError()
    {
        var (service, _) = CreateService();
        var result = await service.SimpleLoginAsync(new SimpleLoginRequest { EmailOrPhone = "", Password = "" });

        Assert.Equal("Bạn cần cung cấp email/số điện thoại và mật khẩu.", result.ErrorMessage);
    }

    [Fact]
    public async Task WrongPassword_ReturnsIncorrectCredentialsError()
    {
        var (service, db) = CreateService();
        db.Users.Add(NewUser(email: "a@test.local", phoneVerified: true, status: 1));
        await db.SaveChangesAsync();

        var result = await service.SimpleLoginAsync(new SimpleLoginRequest { EmailOrPhone = "a@test.local", Password = "WrongPass" });

        Assert.Equal("Email hoặc mật khẩu không đúng.", result.ErrorMessage);
    }

    [Fact]
    public async Task DisabledAccount_ReturnsLockedError()
    {
        var (service, db) = CreateService();
        db.Users.Add(NewUser(email: "a@test.local", phoneVerified: true, status: 0));
        await db.SaveChangesAsync();

        var result = await service.SimpleLoginAsync(new SimpleLoginRequest { EmailOrPhone = "a@test.local", Password = "Passw0rd!23" });

        Assert.Equal("Tài khoản đã bị khóa.", result.ErrorMessage);
    }

    [Fact]
    public async Task NonInternalAccountWithoutPhone_RequiresPhoneInput()
    {
        var (service, db) = CreateService();
        var user = NewUser(email: "a@test.local", phoneVerified: false, status: 1);
        user.Phone = null;
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var result = await service.SimpleLoginAsync(new SimpleLoginRequest { EmailOrPhone = "a@test.local", Password = "Passw0rd!23" });

        Assert.True(result.RequiresPhoneInput);
    }

    [Fact]
    public async Task NonInternalAccountWithUnverifiedPhone_RequiresPhoneVerification()
    {
        var (service, db) = CreateService();
        db.Users.Add(NewUser(email: "a@test.local", phoneVerified: false, status: 1));
        await db.SaveChangesAsync();

        var result = await service.SimpleLoginAsync(new SimpleLoginRequest { EmailOrPhone = "a@test.local", Password = "Passw0rd!23" });

        Assert.True(result.RequiresPhoneVerification);
    }

    // Both success-path tests below hit UserRepository.UpdateLastLoginAtAsync, which uses
    // EF Core's ExecuteUpdateAsync — unsupported by the InMemory provider ("The methods
    // 'ExecuteUpdate' and 'ExecuteUpdateAsync' are not supported by the current database
    // provider"). Needs a real relational provider (Postgres/SQLite) to cover; not a unit test.
    [Fact(Skip = "ExecuteUpdateAsync (UpdateLastLoginAtAsync) is not supported by EF Core InMemory — needs integration test against a real DB")]
    public async Task ValidActiveVerifiedAccount_ReturnsTokens()
    {
        var (service, db) = CreateService();
        db.Users.Add(NewUser(email: "a@test.local", phoneVerified: true, status: 1));
        await db.SaveChangesAsync();

        var result = await service.SimpleLoginAsync(new SimpleLoginRequest { EmailOrPhone = "a@test.local", Password = "Passw0rd!23" });

        Assert.True(string.IsNullOrEmpty(result.ErrorMessage), result.ErrorMessage);
        Assert.NotNull(result.AccessToken);
        Assert.NotNull(result.RefreshToken);
    }

    [Fact(Skip = "ExecuteUpdateAsync (UpdateLastLoginAtAsync) is not supported by EF Core InMemory — needs integration test against a real DB")]
    public async Task InternalStaffAccountWithoutPhone_SkipsPhoneGateAndLogsIn()
    {
        var (service, db) = CreateService();
        var staff = NewUser(email: "staff@test.local", phoneVerified: false, status: 1);
        staff.Phone = null;
        staff.Primaryrole = UserRole.Staff;
        db.Users.Add(staff);
        await db.SaveChangesAsync();

        var result = await service.SimpleLoginAsync(new SimpleLoginRequest { EmailOrPhone = "staff@test.local", Password = "Passw0rd!23" });

        Assert.True(string.IsNullOrEmpty(result.ErrorMessage));
        Assert.NotNull(result.AccessToken);
    }

    private static User NewUser(string email, bool phoneVerified, int status) => new()
    {
        Userid = Guid.NewGuid().ToString(),
        Email = email,
        Phone = "0901111111",
        Password = Passwords.HashPassword("Passw0rd!23"),
        Fullname = "Nguyễn Văn A",
        Isphoneverified = phoneVerified,
        Primaryrole = UserRole.Student,
        Status = status,
        Createdat = DateTime.UtcNow
    };

    private static (SimpleAuthService Service, AgoraDbContext Db) CreateService()
    {
        var db = TestSupport.CreateInMemoryContext("login-async");
        var unitOfWork = new UnitOfWork(db, Passwords, NullLogger<UnitOfWork>.Instance);
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        var service = new SimpleAuthService(
            unitOfWork,
            Passwords,
            new FakeAuthenticationRepository(),
            new FakeOtpSender(),
            configuration,
            NullLogger<SimpleAuthService>.Instance,
            new FakeDistributedCache(),
            null!);
        return (service, db);
    }
}
