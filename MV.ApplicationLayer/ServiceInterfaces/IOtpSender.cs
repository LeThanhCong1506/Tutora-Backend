namespace MV.ApplicationLayer.ServiceInterfaces
{
    /// <summary>
    /// Gửi mã OTP tới số điện thoại. Tách interface để dễ thay kênh gửi
    /// (SMS provider / Zalo ZNS) mà không sửa logic xác thực.
    /// </summary>
    public interface IOtpSender
    {
        Task SendOtpAsync(string phone, string otpCode);
    }
}
