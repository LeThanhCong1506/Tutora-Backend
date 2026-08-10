namespace MV.ApplicationLayer.ServiceInterfaces
{
    /// <summary>
    /// OTP xác thực giao dịch lớn (&gt;= <see cref="MV.DomainLayer.Constants.LargeTransactionPolicy.ThresholdAmount"/>)
    /// của học sinh tự đăng ký, gửi tới SĐT phụ huynh qua ZNS. Độc lập hoàn toàn với OTP xác thực
    /// đăng nhập trong <c>SimpleAuthService</c> — không set <c>Isphoneverified</c>, không cấp token.
    /// </summary>
    public interface ILargeTransactionOtpService
    {
        /// <summary>
        /// Sinh mã, lưu Redis, gửi qua ZNS tới <paramref name="parentPhone"/>.
        /// Ném exception (cooldown / vượt giới hạn ngày) nếu bị chặn gửi.
        /// </summary>
        Task SendAsync(int bookingId, string phase, string parentPhone);

        /// <summary>
        /// Kiểm mã. Đúng thì ghi cờ đã duyệt cho booking+phase này (TTL đủ để bấm trả tiền ngay sau).
        /// Ném exception nếu sai/hết hạn/không tìm thấy/quá số lần thử.
        /// </summary>
        Task VerifyAsync(int bookingId, string phase, string code);

        /// <summary>Đã có xác nhận OTP còn hiệu lực cho booking+phase này chưa.</summary>
        Task<bool> IsApprovedAsync(int bookingId, string phase);
    }
}
