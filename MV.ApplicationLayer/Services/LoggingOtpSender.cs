using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.ServiceInterfaces;

namespace MV.ApplicationLayer.Services
{
    /// <summary>
    /// Stub OTP sender: chỉ ghi mã OTP ra log (dùng cho dev khi CHƯA tích hợp kênh gửi thật).
    /// ⚠️ PRODUCTION: thay class này bằng implementation gửi SMS hoặc Zalo ZNS
    /// (xem ZaloOAService.SendZnsTemplateAsync) rồi đổi đăng ký DI trong Program.cs.
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
