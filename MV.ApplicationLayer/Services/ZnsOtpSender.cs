using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.ServiceInterfaces;

namespace MV.ApplicationLayer.Services;

public class ZnsOtpSender : IOtpSender
{
    private readonly IZaloOAService _zaloOA;
    private readonly ILogger<ZnsOtpSender> _logger;

    public ZnsOtpSender(IZaloOAService zaloOA, ILogger<ZnsOtpSender> logger)
    {
        _zaloOA = zaloOA;
        _logger = logger;
    }

    public async Task SendOtpAsync(string phone, string otpCode)
    {
        var result = await _zaloOA.SendZnsOtpAsync(phone, otpCode);
        if (!result.Success)
        {
            _logger.LogWarning(
                "[OTP] Gửi ZNS thất bại ({Error}). phone={Phone}",
                result.Error,
                phone);
        }
    }
}
