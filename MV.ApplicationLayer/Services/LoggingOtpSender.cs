using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.ServiceInterfaces;

namespace MV.ApplicationLayer.Services
{
    /// <summary>
    /// Sender dự phòng chỉ ghi mã OTP ra log, dùng khi cần tách khỏi ZNS để debug.
    /// </summary>
    public class LoggingOtpSender : IOtpSender
    {
        private readonly ILogger<LoggingOtpSender> _logger;

        public LoggingOtpSender(ILogger<LoggingOtpSender> logger)
        {
            _logger = logger;
        }

        public Task SendOtpAsync(string phone, string otpCode)
        {
            _logger.LogWarning(
                "[OTP STUB] Mã OTP {Otp} cho số {Phone} (hiệu lực 10 phút). " +
                "Chưa tích hợp SMS/ZNS — thay LoggingOtpSender bằng sender thật trước khi lên production.",
                otpCode, phone);
            return Task.CompletedTask;
        }
    }
}
