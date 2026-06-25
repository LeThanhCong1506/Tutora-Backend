namespace MV.ApplicationLayer.ServiceInterfaces
{
    /// <summary>
    /// Gửi mã OTP tới số điện thoại qua kênh đã cấu hình.
    /// </summary>
    public interface IOtpSender
    {
        Task SendOtpAsync(string phone, string otpCode);
    }
}
