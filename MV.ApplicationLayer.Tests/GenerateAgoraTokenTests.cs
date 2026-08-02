using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MV.ApplicationLayer.Services;
using MV.DomainLayer.Configuration;
using Xunit;

namespace MV.ApplicationLayer.Tests;

// Maps to Excel sheet "GenerateAgoraToken" (Code_27, AgoraRTCService.GenerateToken).
public class GenerateAgoraTokenTests
{
    [Fact]
    public void MissingAppIdOrCertificate_Throws()
    {
        var service = CreateService(new AgoraSettings { AppId = "", AppCertificate = "" });

        Assert.Throws<InvalidOperationException>(() => service.GenerateToken("session-1", "user-1"));
    }

    [Fact]
    public void BlankChannelName_Throws()
    {
        var service = CreateService(ValidSettings());

        Assert.Throws<ArgumentException>(() => service.GenerateToken("", "user-1"));
    }

    [Fact]
    public void ValidChannelAndAccount_ReturnsNonEmptyToken()
    {
        var service = CreateService(ValidSettings());

        var token = service.GenerateToken("session-1", "user-1");

        Assert.False(string.IsNullOrWhiteSpace(token));
    }

    private static AgoraSettings ValidSettings() => new()
    {
        AppId = "test-app-id",
        AppCertificate = "0123456789abcdef0123456789abcdef",
        TokenExpireSeconds = 3600
    };

    private static AgoraRTCService CreateService(AgoraSettings settings) =>
        new(Options.Create(settings), NullLogger<AgoraRTCService>.Instance);
}
