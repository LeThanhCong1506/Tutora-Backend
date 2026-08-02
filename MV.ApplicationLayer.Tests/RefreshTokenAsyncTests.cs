using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MV.ApplicationLayer.Services;
using MV.DomainLayer.Constants;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Helpers;
using MV.InfrastructureLayer;
using MV.InfrastructureLayer.DBContext;
using MV.InfrastructureLayer.Repositories;
using Xunit;

namespace MV.ApplicationLayer.Tests;

// Maps to Excel sheet "RefreshTokenAsync" (Code_7, RefreshTokenService.RefreshAsync).
public class RefreshTokenAsyncTests
{
    private const string UserId = "user-1";
    private const string RawRefreshToken = "raw-refresh-token";

    [Fact]
    public async Task InvalidAccessToken_ReturnsError()
    {
        var ctx = CreateService();
        ctx.Auth.PrincipalToReturn = null;

        var result = await ctx.Service.RefreshAsync("expired-access-token", RawRefreshToken);

        Assert.Equal("Access token không hợp lệ.", result.ErrorMessage);
    }

    [Fact]
    public async Task RefreshTokenNotFound_ReturnsError()
    {
        var ctx = CreateService();
        ctx.Auth.PrincipalToReturn = FakeAuthenticationRepository.PrincipalFor(UserId);

        var result = await ctx.Service.RefreshAsync("expired-access-token", RawRefreshToken);

        Assert.Equal("Refresh token không tồn tại.", result.ErrorMessage);
    }

    [Fact]
    public async Task AlreadyRevokedToken_RevokesWholeFamilyAndReturnsError()
    {
        var ctx = CreateService();
        ctx.Auth.PrincipalToReturn = FakeAuthenticationRepository.PrincipalFor(UserId);
        var otherTokenSameFamily = NewToken(id: "other-token", revoked: false);
        otherTokenSameFamily.Tokenhash = "hash-some-other-raw-token";
        ctx.Db.Refreshtokens.AddRange(
            NewToken(id: "used-token", revoked: true),
            otherTokenSameFamily);
        await ctx.Db.SaveChangesAsync();

        var result = await ctx.Service.RefreshAsync("expired-access-token", RawRefreshToken);

        Assert.Equal("Refresh token đã bị thu hồi. Vui lòng đăng nhập lại.", result.ErrorMessage);
        var stillActive = await ctx.Db.Refreshtokens.AsNoTracking().SingleAsync(t => t.Id == "other-token");
        Assert.NotNull(stillActive.Revokedat);
    }

    [Fact]
    public async Task ExpiredToken_ReturnsError()
    {
        var ctx = CreateService();
        ctx.Auth.PrincipalToReturn = FakeAuthenticationRepository.PrincipalFor(UserId);
        ctx.Db.Refreshtokens.Add(NewToken(id: "expired-token", revoked: false, expiresAt: TimeZoneHelper.UtcNow.AddDays(-1)));
        await ctx.Db.SaveChangesAsync();

        var result = await ctx.Service.RefreshAsync("expired-access-token", RawRefreshToken);

        Assert.Equal("Refresh token đã hết hạn. Vui lòng đăng nhập lại.", result.ErrorMessage);
    }

    [Fact]
    public async Task TokenBelongsToDifferentUser_ReturnsInvalidTokenError()
    {
        var ctx = CreateService();
        ctx.Auth.PrincipalToReturn = FakeAuthenticationRepository.PrincipalFor(UserId);
        var token = NewToken(id: "mismatched-token", revoked: false);
        token.Userid = "someone-else";
        ctx.Db.Refreshtokens.Add(token);
        await ctx.Db.SaveChangesAsync();

        var result = await ctx.Service.RefreshAsync("expired-access-token", RawRefreshToken);

        Assert.Equal("Token không hợp lệ.", result.ErrorMessage);
    }

    [Fact]
    public async Task UserDisabled_ReturnsAccountLockedError()
    {
        var ctx = CreateService();
        ctx.Auth.PrincipalToReturn = FakeAuthenticationRepository.PrincipalFor(UserId);
        ctx.Db.Refreshtokens.Add(NewToken(id: "valid-token", revoked: false));
        ctx.Db.Users.Add(NewUser(status: 0, phoneVerified: true));
        await ctx.Db.SaveChangesAsync();

        var result = await ctx.Service.RefreshAsync("expired-access-token", RawRefreshToken);

        Assert.Equal("Tài khoản không tồn tại hoặc đã bị khóa.", result.ErrorMessage);
    }

    [Fact]
    public async Task NonInternalUserWithUnverifiedPhone_RevokesAllTokensAndRequiresVerification()
    {
        var ctx = CreateService();
        ctx.Auth.PrincipalToReturn = FakeAuthenticationRepository.PrincipalFor(UserId);
        ctx.Db.Refreshtokens.Add(NewToken(id: "valid-token", revoked: false));
        ctx.Db.Users.Add(NewUser(status: 1, phoneVerified: false));
        await ctx.Db.SaveChangesAsync();

        var result = await ctx.Service.RefreshAsync("expired-access-token", RawRefreshToken);

        Assert.True(result.RequiresPhoneVerification);
        var stillActive = await ctx.Db.Refreshtokens.AsNoTracking().SingleAsync(t => t.Id == "valid-token");
        Assert.NotNull(stillActive.Revokedat);
    }

    [Fact]
    public async Task ValidRequest_RotatesRefreshTokenInSameFamily()
    {
        var ctx = CreateService();
        ctx.Auth.PrincipalToReturn = FakeAuthenticationRepository.PrincipalFor(UserId);
        var oldToken = NewToken(id: "valid-token", revoked: false);
        ctx.Db.Refreshtokens.Add(oldToken);
        ctx.Db.Users.Add(NewUser(status: 1, phoneVerified: true));
        await ctx.Db.SaveChangesAsync();

        var result = await ctx.Service.RefreshAsync("expired-access-token", RawRefreshToken);

        Assert.True(string.IsNullOrEmpty(result.ErrorMessage));
        Assert.NotNull(result.AccessToken);
        Assert.NotNull(result.RefreshToken);
        var revokedOld = await ctx.Db.Refreshtokens.AsNoTracking().SingleAsync(t => t.Id == "valid-token");
        Assert.NotNull(revokedOld.Revokedat);
        var newToken = await ctx.Db.Refreshtokens.AsNoTracking().SingleAsync(t => t.Id != "valid-token");
        Assert.Equal("family-1", newToken.Tokenfamily);
    }

    private static RefreshToken NewToken(string id, bool revoked, DateTime? expiresAt = null) => new()
    {
        Id = id,
        Tokenhash = $"hash-{RawRefreshToken}",
        Userid = UserId,
        Tokenfamily = "family-1",
        Expiresat = expiresAt ?? TimeZoneHelper.UtcNow.AddDays(7),
        Createdat = TimeZoneHelper.UtcNow,
        Revokedat = revoked ? TimeZoneHelper.UtcNow.AddMinutes(-1) : null
    };

    private static User NewUser(int status, bool phoneVerified) => new()
    {
        Userid = UserId,
        Phone = "0901111111",
        Password = "hash",
        Fullname = "Nguyễn Văn A",
        Isphoneverified = phoneVerified,
        Primaryrole = UserRole.Student,
        Status = status,
        Createdat = DateTime.UtcNow
    };

    private static ServiceContext CreateService()
    {
        var db = TestSupport.CreateInMemoryContext("refresh-token");
        var unitOfWork = new UnitOfWork(db, new PasswordRepository(), NullLogger<UnitOfWork>.Instance);
        var auth = new FakeAuthenticationRepository();
        var service = new RefreshTokenService(
            unitOfWork,
            auth,
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
            NullLogger<RefreshTokenService>.Instance);
        return new ServiceContext(service, db, auth);
    }

    private sealed record ServiceContext(RefreshTokenService Service, AgoraDbContext Db, FakeAuthenticationRepository Auth);
}
