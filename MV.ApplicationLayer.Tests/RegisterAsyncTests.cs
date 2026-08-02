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

// Maps to Excel sheet "RegisterAsync" (Code_4, SimpleAuthService.SimpleRegisterAsync).
// This method never throws for validation failures — it returns a TokenResponse with
// ErrorMessage populated, so tests assert on ErrorMessage, not on thrown exceptions.
public class RegisterAsyncTests
{
    [Fact]
    public async Task BlankPhone_ReturnsRequiredError()
    {
        await using var db = CreateContext();
        var service = CreateService(db);
        var result = await service.SimpleRegisterAsync(new SimpleRegisterRequest { Phone = "", Password = "Passw0rd!23", FullName = "Nguyễn Văn A" });

        Assert.Equal("Số điện thoại là bắt buộc.", result.ErrorMessage);
    }

    [Fact]
    public async Task BlankPassword_ReturnsRequiredError()
    {
        await using var db = CreateContext();
        var service = CreateService(db);
        var result = await service.SimpleRegisterAsync(new SimpleRegisterRequest { Phone = "0901111111", Password = "", FullName = "Nguyễn Văn A" });

        Assert.Equal("Mật khẩu là bắt buộc.", result.ErrorMessage);
    }

    [Fact]
    public async Task BlankFullName_ReturnsRequiredError()
    {
        await using var db = CreateContext();
        var service = CreateService(db);
        var result = await service.SimpleRegisterAsync(new SimpleRegisterRequest { Phone = "0901111111", Password = "Passw0rd!23", FullName = "" });

        Assert.Equal("Tên đầy đủ là bắt buộc.", result.ErrorMessage);
    }

    [Fact]
    public async Task AdminRole_NotSelfRegisterable_ReturnsError()
    {
        await using var db = CreateContext();
        var service = CreateService(db);
        var result = await service.SimpleRegisterAsync(new SimpleRegisterRequest
        {
            Phone = "0901111111",
            Password = "Passw0rd!23",
            FullName = "Nguyễn Văn A",
            Role = UserRole.Admin
        });

        Assert.Equal("Chức vụ này không cho phép tự đăng ký.", result.ErrorMessage);
    }

    [Fact]
    public async Task ShortPassword_IsNotRejected_NoLengthValidationExists()
    {
        // Excel spec assumed a min-length check on Password ("123 quá ngắn" -> rejected),
        // but the real code only checks IsNullOrEmpty. This proves that assumption is false.
        await using var db = CreateContext();
        var service = CreateService(db);

        var result = await service.SimpleRegisterAsync(new SimpleRegisterRequest
        {
            Phone = "0901111111",
            Password = "123",
            FullName = "Nguyễn Văn A",
            Role = UserRole.Student
        });

        Assert.True(string.IsNullOrEmpty(result.ErrorMessage));
        Assert.True(result.RequiresPhoneVerification);
    }

    [Fact]
    public async Task ExistingVerifiedPhone_ReturnsAlreadyUsedError()
    {
        await using var db = CreateContext();
        db.Users.Add(new User
        {
            Userid = "existing-verified",
            Phone = "0902222222",
            Password = "hash",
            Fullname = "Đã xác thực",
            Isphoneverified = true,
            Primaryrole = UserRole.Student,
            Status = 1,
            Createdat = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.SimpleRegisterAsync(new SimpleRegisterRequest
        {
            Phone = "0902222222",
            Password = "Passw0rd!23",
            FullName = "Người khác",
            Role = UserRole.Student
        });

        Assert.Equal("Số điện thoại đã được sử dụng.", result.ErrorMessage);
    }

    [Fact]
    public async Task ExistingUnverifiedPhone_ResumesRegistration_ResendsOtpInsteadOfError()
    {
        await using var db = CreateContext();
        db.Users.Add(new User
        {
            Userid = "existing-unverified",
            Phone = "0903333333",
            Password = "old-hash",
            Fullname = "Tên cũ",
            Isphoneverified = false,
            Primaryrole = UserRole.Student,
            Status = 1,
            Createdat = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var service = CreateService(db);

        var result = await service.SimpleRegisterAsync(new SimpleRegisterRequest
        {
            Phone = "0903333333",
            Password = "Passw0rd!23",
            FullName = "Tên mới cập nhật",
            Role = UserRole.Student
        });

        Assert.True(string.IsNullOrEmpty(result.ErrorMessage));
        Assert.True(result.RequiresPhoneVerification);
        var updated = await db.Users.AsNoTracking().SingleAsync(u => u.Phone == "0903333333");
        Assert.Equal("Tên mới cập nhật", updated.Fullname);
    }

    [Fact]
    public async Task NewValidStudentRegistration_CreatesUserAndRequiresPhoneVerification()
    {
        await using var db = CreateContext();
        var service = CreateService(db);

        var result = await service.SimpleRegisterAsync(new SimpleRegisterRequest
        {
            Phone = "0901111111",
            Password = "Passw0rd!23",
            FullName = "Nguyễn Văn A",
            Role = UserRole.Student
        });

        Assert.True(string.IsNullOrEmpty(result.ErrorMessage));
        Assert.True(result.RequiresPhoneVerification);
        var created = await db.Users.AsNoTracking().SingleAsync(u => u.Phone == "0901111111");
        Assert.False(created.Isphoneverified);
        Assert.Equal(UserRole.Student, created.Primaryrole);
    }

    private static SimpleAuthService CreateService(AgoraDbContext db)
    {
        var unitOfWork = new UnitOfWork(db, new PasswordRepository(), NullLogger<UnitOfWork>.Instance);
        return new SimpleAuthService(
            unitOfWork,
            new PasswordRepository(),
            null!,
            new FakeOtpSender(),
            null!,
            NullLogger<SimpleAuthService>.Instance,
            new FakeDistributedCache(),
            null!);
    }

    private static AgoraDbContext CreateContext() => TestSupport.CreateInMemoryContext("register-async");
}
