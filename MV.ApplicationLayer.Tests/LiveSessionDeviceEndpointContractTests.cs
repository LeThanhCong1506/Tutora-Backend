using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.Hubs;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.ApplicationLayer.Services;
using MV.ApplicationLayer.Services.Agora;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;
using MV.PresentationLayer.Controllers;
using Xunit;

namespace MV.ApplicationLayer.Tests;

public class LiveSessionDeviceEndpointContractTests
{
    [Theory]
    [InlineData(nameof(AgoraController.JoinRoom), "room/{classSessionId:int}/join")]
    [InlineData(nameof(AgoraController.TakeOverRoom), "room/{classSessionId:int}/takeover")]
    [InlineData(nameof(AgoraController.Heartbeat), "room/{classSessionId:int}/heartbeat")]
    [InlineData(nameof(AgoraController.Leave), "room/{classSessionId:int}/leave")]
    [InlineData(nameof(AgoraController.GetWhiteboardRoom), "whiteboard/{classSessionId:int}")]
    public void MutatingRoomEndpoints_ArePostOnly(string methodName, string expectedTemplate)
    {
        var method = typeof(AgoraController).GetMethod(methodName);
        var attribute = method?.GetCustomAttributes(typeof(HttpPostAttribute), inherit: true)
            .Cast<HttpPostAttribute>()
            .SingleOrDefault();

        Assert.NotNull(attribute);
        Assert.Equal(expectedTemplate, attribute!.Template);
        Assert.Empty(method!.GetCustomAttributes(typeof(HttpGetAttribute), inherit: true));
    }

    [Fact]
    public void TakeoverRequest_RequiresExpectedLeaseAndBoundedDeviceLabel()
    {
        var request = new LiveSessionTakeoverRequest
        {
            ParticipationId = Guid.NewGuid().ToString(),
            DeviceId = Guid.NewGuid().ToString(),
            DeviceLabel = new string('x', 121)
        };
        var results = Validate(request);

        Assert.Contains(results, result => result.MemberNames.Contains(nameof(request.ExpectedActiveLeaseId)));
        Assert.Contains(results, result => result.MemberNames.Contains(nameof(request.DeviceLabel)));
    }

    [Fact]
    public void LegacyRoomGet_FailsBeforeIssuingAnyToken()
    {
        var controller = new AgoraController(
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!);

        var result = Assert.IsType<BadRequestObjectResult>(controller.GetRoomInfo(42));
        var response = Assert.IsType<APIResponse<object>>(result.Value);
        var code = response.Error?.GetType().GetProperty("code")?.GetValue(response.Error);

        Assert.Equal(400, response.StatusCode);
        Assert.Equal(LiveSessionDeviceErrorCodes.DeviceSessionRequired, code);
        Assert.Null(response.Content);
    }

    [Fact]
    public void LiveSessionHub_IsAuthorizedAndExposesLeaseRegistration()
    {
        Assert.NotNull(typeof(LiveSessionHub).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).SingleOrDefault());

        var register = typeof(LiveSessionHub).GetMethod(nameof(LiveSessionHub.RegisterSession));
        Assert.NotNull(register);
        Assert.Equal(
            [typeof(int), typeof(string), typeof(string)],
            register!.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
    }

    [Fact]
    public void LeaseServiceContract_RequiresCompareCredentialsForRenewAndRelease()
    {
        // Device ownership must survive suspended mobile/Zalo timers for the duration of a lesson.
        Assert.Equal(4 * 60 * 60, LiveSessionDeviceLeaseService.LeaseTtlSeconds);
        Assert.Equal(120, AgoraRTCService.LiveSessionTokenMaxSeconds);

        var service = typeof(ILiveSessionDeviceLeaseService);

        foreach (var methodName in new[]
                 {
                     nameof(ILiveSessionDeviceLeaseService.RenewAsync),
                     nameof(ILiveSessionDeviceLeaseService.IsActiveAsync),
                     nameof(ILiveSessionDeviceLeaseService.ReleaseAsync)
                 })
        {
            var parameters = service.GetMethod(methodName)!.GetParameters();
            Assert.Contains(parameters, parameter => parameter.Name == "participationId");
            Assert.Contains(parameters, parameter => parameter.Name == "leaseId");
        }
    }

    [Fact]
    public void AgoraChannels_AreIsolatedPerClassSessionEvenWithinTheSameBooking()
    {
        var first = AgoraChannelName.ForSession(101, bookingId: 7);
        var second = AgoraChannelName.ForSession(102, bookingId: 7);

        Assert.Equal("101", first);
        Assert.Equal("102", second);
        Assert.NotEqual(first, second);
    }

    private static IReadOnlyList<ValidationResult> Validate(object value)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(value, new ValidationContext(value), results, validateAllProperties: true);
        return results;
    }
}
