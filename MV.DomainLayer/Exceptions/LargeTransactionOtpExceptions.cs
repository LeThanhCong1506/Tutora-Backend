namespace MV.DomainLayer.Exceptions
{
    /// <summary>
    /// Học sinh tự đăng ký chưa nhập SĐT phụ huynh, nên không có đích để gửi OTP giao dịch lớn.
    /// FE bắt riêng exception này (message cố định, dễ nhận diện) để biết cần điều hướng
    /// người dùng sang trang hồ sơ thay vì hiện màn nhập OTP.
    /// </summary>
    public class ParentPhoneRequiredForLargeTransactionException : BadRequestException
    {
        public ParentPhoneRequiredForLargeTransactionException()
            : base("Vui lòng cập nhật số điện thoại phụ huynh trước khi thực hiện giao dịch từ 2.000.000đ trở lên.") { }
    }

    /// <summary>
    /// Chặn ở đúng lúc tiền sắp bị trừ (trừ ví / tạo link PayOS): giao dịch đủ lớn nhưng
    /// chưa có xác nhận OTP còn hiệu lực cho booking + phase này.
    /// </summary>
    public class LargeTransactionOtpRequiredException : BadRequestException
    {
        public string Phase { get; }

        public LargeTransactionOtpRequiredException(string phase)
            : base("Giao dịch từ 2.000.000đ trở lên cần xác thực OTP gửi tới số điện thoại phụ huynh trước khi tiếp tục.")
        {
            Phase = phase;
        }
    }

    public class OtpExpiredException : BadRequestException
    {
        public OtpExpiredException()
            : base("Mã OTP đã hết hạn. Vui lòng gửi lại mã mới.") { }
    }

    public class OtpInvalidException : BadRequestException
    {
        public OtpInvalidException()
            : base("Mã OTP không hợp lệ.") { }
    }

    public class OtpTooManyAttemptsException : BadRequestException
    {
        public OtpTooManyAttemptsException()
            : base("Quá nhiều lần nhập OTP không hợp lệ. Vui lòng gửi lại mã mới.") { }
    }

    public class OtpCooldownActiveException : BadRequestException
    {
        public OtpCooldownActiveException(int retryAfterSeconds)
            : base($"Vui lòng đợi {retryAfterSeconds} giây trước khi gửi lại mã OTP.")
        {
            RetryAfterSeconds = retryAfterSeconds;
        }

        public int RetryAfterSeconds { get; }
    }

    public class OtpDailyLimitExceededException : BadRequestException
    {
        public OtpDailyLimitExceededException()
            : base("Bạn đã gửi mã OTP quá số lần cho phép trong ngày. Vui lòng thử lại vào ngày mai.") { }
    }
}
