namespace MV.DomainLayer.Constants;

public static class PayoutConstants
{
    public static class Status
    {
        public const string Pending = "Pending";
        public const string Processing = "Processing";
        public const string Completed = "Completed";
        public const string Failed = "Failed";
        public const string Rejected = "Rejected";
        public const string Unknown = "Unknown";
        public const string Error = "Error";
    }

    public static class PayOSStatus
    {
        public const string Pending = "PENDING";
        public const string Success = "SUCCESS";
        public const string Failed = "FAILED";
        public const string Processing = "PROCESSING";
    }

    public static class ErrorCodes
    {
        public const string InsufficientBalance = "INSUFFICIENT_BALANCE";
        public const string InvalidBankAccount = "INVALID_BANK_ACCOUNT";
        public const string DuplicateOrder = "DUPLICATE_ORDER";
        public const string Timeout = "TIMEOUT";
        public const string NetworkError = "NETWORK_ERROR";
        public const string MaxRetriesExceeded = "MAX_RETRIES_EXCEEDED";
        public const string Unknown = "UNKNOWN_ERROR";
    }

    public static class ErrorMessages
    {
        public const string InsufficientBalance = "Số dư ví PayOS không đủ. Vui lòng thử lại sau.";
        public const string InvalidBankAccount = "Thông tin tài khoản ngân hàng không hợp lệ. Vui lòng cập nhật thông tin ngân hàng.";
        public const string DuplicateOrder = "Yêu cầu rút tiền này đã được xử lý.";
        public const string Timeout = "Yêu cầu rút tiền bị timeout. Hệ thống sẽ tự động thử lại.";
        public const string NetworkError = "Lỗi kết nối mạng. Hệ thống sẽ tự động thử lại.";
        public const string MaxRetriesExceeded = "Đã vượt quá số lần thử lại tối đa. Vui lòng liên hệ hỗ trợ.";
        public const string Unknown = "Đã xảy ra lỗi không xác định. Vui lòng liên hệ hỗ trợ.";
    }

    public static class AlertTypes
    {
        public const string LowBalance = "LOW_BALANCE";
        public const string CriticalBalance = "CRITICAL_BALANCE";
        public const string PayoutFailed = "PAYOUT_FAILED";
        public const string MaxRetriesExceeded = "MAX_RETRIES_EXCEEDED";
        public const string StuckPayout = "STUCK_PAYOUT";
    }

    public static class AlertSeverity
    {
        public const string Normal   = "normal";
        public const string Info     = "info";
        public const string Warning  = "warning";
        public const string Critical = "critical";
    }
}
