namespace MV.DomainLayer.Exceptions
{
    /// <summary>
    /// Chưa có/chưa xác thực SĐT riêng của chính người dùng — không có đích để gửi OTP xác thực
    /// tài khoản ngân hàng (khác OTP giao dịch lớn, vốn gửi tới SĐT phụ huynh).
    /// </summary>
    public class BankAccountOtpPhoneNotVerifiedException : BadRequestException
    {
        public BankAccountOtpPhoneNotVerifiedException()
            : base("Vui lòng xác thực số điện thoại của bạn trước khi thao tác với tài khoản ngân hàng.") { }
    }

    /// <summary>
    /// Học sinh do phụ huynh quản lý (không tự đăng ký) không được lưu/xoá tài khoản ngân hàng —
    /// tính năng này chỉ dành cho chủ tài khoản có SĐT riêng đã xác thực.
    /// </summary>
    public class BankAccountNotEligibleException : BadRequestException
    {
        public BankAccountNotEligibleException()
            : base("Tài khoản này không thể tự quản lý tài khoản ngân hàng.") { }
    }

    /// <summary>
    /// Chặn ở đúng lúc lưu/xoá tài khoản ngân hàng: chưa có xác nhận OTP còn hiệu lực cho user này.
    /// </summary>
    public class BankAccountOtpRequiredException : BadRequestException
    {
        public BankAccountOtpRequiredException()
            : base("Vui lòng xác thực OTP trước khi lưu/xoá tài khoản ngân hàng.") { }
    }

    public class BankAccountOtpExpiredException : BadRequestException
    {
        public BankAccountOtpExpiredException()
            : base("Mã OTP đã hết hạn. Vui lòng gửi lại mã mới.") { }
    }

    public class BankAccountOtpInvalidException : BadRequestException
    {
        public BankAccountOtpInvalidException()
            : base("Mã OTP không hợp lệ.") { }
    }

    public class BankAccountOtpTooManyAttemptsException : BadRequestException
    {
        public BankAccountOtpTooManyAttemptsException()
            : base("Quá nhiều lần nhập OTP không hợp lệ. Vui lòng gửi lại mã mới.") { }
    }

    public class BankAccountOtpCooldownActiveException : BadRequestException
    {
        public BankAccountOtpCooldownActiveException(int retryAfterSeconds)
            : base($"Vui lòng đợi {retryAfterSeconds} giây trước khi gửi lại mã OTP.")
        {
            RetryAfterSeconds = retryAfterSeconds;
        }

        public int RetryAfterSeconds { get; }
    }

    public class BankAccountOtpDailyLimitExceededException : BadRequestException
    {
        public BankAccountOtpDailyLimitExceededException()
            : base("Bạn đã gửi mã OTP quá số lần cho phép trong ngày. Vui lòng thử lại vào ngày mai.") { }
    }
}
