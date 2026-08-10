namespace MV.ApplicationLayer.ServiceInterfaces
{
    /// <summary>
    /// OTP xác thực thao tác lưu/xoá tài khoản ngân hàng, gửi tới SĐT riêng của chính chủ tài
    /// khoản (Tutor/Parent/Student tự đăng ký) qua ZNS. Độc lập hoàn toàn với OTP đăng nhập
    /// (<c>SimpleAuthService</c>) và OTP giao dịch lớn (<c>ILargeTransactionOtpService</c>) —
    /// không set <c>Isphoneverified</c>, không cấp token.
    /// </summary>
    public interface IBankAccountOtpService
    {
        /// <summary>
        /// Sinh mã, lưu Redis, gửi qua ZNS tới <paramref name="phone"/>.
        /// Ném exception (cooldown / vượt giới hạn ngày) nếu bị chặn gửi.
        /// </summary>
        Task SendAsync(string userId, string phone);

        /// <summary>
        /// Kiểm mã. Đúng thì ghi cờ đã duyệt cho user này (TTL đủ để lưu/xoá ngay sau).
        /// Ném exception nếu sai/hết hạn/không tìm thấy/quá số lần thử.
        /// </summary>
        Task VerifyAsync(string userId, string code);

        /// <summary>Đã có xác nhận OTP còn hiệu lực cho user này chưa.</summary>
        Task<bool> IsApprovedAsync(string userId);

        /// <summary>
        /// Dùng cờ approved 1 lần rồi xoá — bắt buộc gọi ngay sau khi lưu/xoá thành công. Khác
        /// <see cref="MV.ApplicationLayer.Services.LargeTransactionOtpService"/> (khoá theo
        /// bookingId+phase nên tự nhiên không tái sử dụng được cho hành động khác), OTP này khoá
        /// theo userId — không tự tiêu thụ tường minh thì 1 lần verify có thể lưu/xoá liên tiếp
        /// nhiều lần trong TTL của cờ approved.
        /// </summary>
        Task ConsumeApprovalAsync(string userId);
    }
}
