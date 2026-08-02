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

// Maps to Excel sheet "CompleteRegistrationAsync" (Code_31, SocialRegistrationService.CompleteRegistrationAsync).
public class CompleteRegistrationAsyncTests
{
    [Fact]
    public async Task ExpiredOrUnknownToken_ReturnsExpiredSessionError()
    {
        var ctx = CreateService();

        var result = await ctx.Service.CompleteRegistrationAsync(new CompleteSocialRegistrationRequest
        {
            SocialRegistrationToken = "no-such-token",
            Phone = "0901111111",
            Role = UserRole.Student
        });

        Assert.Equal("Phiên đăng ký social đã hết hạn. Vui lòng đăng nhập lại bằng Google hoặc Zalo.", result.ErrorMessage);
    }

    [Fact]
    public async Task InvalidPhoneFormat_ReturnsRequiresPhoneInputError()
    {
        var ctx = CreateService();
        var token = await BeginNewGoogleSessionAsync(ctx, "new@gmail.com");

        var result = await ctx.Service.CompleteRegistrationAsync(new CompleteSocialRegistrationRequest
        {
            SocialRegistrationToken = token,
            Phone = "abc",
            Role = UserRole.Student
        });

        Assert.True(result.RequiresPhoneInput);
        Assert.False(string.IsNullOrEmpty(result.ErrorMessage));
    }

    [Fact]
    public async Task NewUserWithoutValidRole_ReturnsRequiresRoleSelection()
    {
        var ctx = CreateService();
        var token = await BeginNewGoogleSessionAsync(ctx, "new2@gmail.com");

        var result = await ctx.Service.CompleteRegistrationAsync(new CompleteSocialRegistrationRequest
        {
            SocialRegistrationToken = token,
            Phone = "0901111111",
            Role = UserRole.Admin
        });

        Assert.True(result.RequiresRoleSelection);
    }

    [Fact]
    public async Task PhoneAlreadyOwnedByAnotherUser_ReturnsAlreadyUsedError()
    {
        var ctx = CreateService();
        ctx.Db.Users.Add(new User
        {
            Userid = "existing-owner",
            Password = "hash",
            Phone = "0902222222",
            Fullname = "Chủ số điện thoại",
            Primaryrole = UserRole.Student,
            Status = 1,
            Createdat = DateTime.UtcNow
        });
        await ctx.Db.SaveChangesAsync();
        var token = await BeginNewGoogleSessionAsync(ctx, "new3@gmail.com");

        var result = await ctx.Service.CompleteRegistrationAsync(new CompleteSocialRegistrationRequest
        {
            SocialRegistrationToken = token,
            Phone = "0902222222",
            Role = UserRole.Student
        });

        Assert.Equal("Số điện thoại đã được sử dụng.", result.ErrorMessage);
    }

    [Fact]
    public async Task ValidPhoneAndRole_SendsOtpAndRequiresPhoneVerification()
    {
        var ctx = CreateService();
        var token = await BeginNewGoogleSessionAsync(ctx, "new4@gmail.com");

        var result = await ctx.Service.CompleteRegistrationAsync(new CompleteSocialRegistrationRequest
        {
            SocialRegistrationToken = token,
            Phone = "0903333333",
            Role = UserRole.Student
        });

        Assert.True(string.IsNullOrEmpty(result.ErrorMessage));
        Assert.True(result.RequiresPhoneVerification);
        Assert.Equal("0903333333", result.Phone);
    }

    private static async Task<string> BeginNewGoogleSessionAsync(ServiceContext ctx, string email)
    {
        var begin = await ctx.Service.BeginAsync(SocialAuthProvider.Google, providerUserId: "google-sub-1", email: email, fullName: "Người dùng mới", avatarUrl: null);
        return begin.SocialRegistrationToken!;
    }

    private static ServiceContext CreateService()
    {
        var db = TestSupport.CreateInMemoryContext("complete-registration");
        var unitOfWork = new UnitOfWork(db, new PasswordRepository(), NullLogger<UnitOfWork>.Instance);
        var service = new SocialRegistrationService(
            unitOfWork,
            new PasswordRepository(),
            new FakeAuthenticationRepository(),
            new FakeOtpSender(),
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
            NullLogger<SocialRegistrationService>.Instance,
            new FakeDistributedCache());
        return new ServiceContext(service, db);
    }

    private sealed record ServiceContext(SocialRegistrationService Service, AgoraDbContext Db);
}
