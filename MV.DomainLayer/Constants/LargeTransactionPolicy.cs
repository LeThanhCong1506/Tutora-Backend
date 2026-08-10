namespace MV.DomainLayer.Constants;

/// <summary>
/// Ngưỡng bắt buộc xác thực OTP (qua ZNS, gửi tới SĐT phụ huynh) trước khi một khoản thanh
/// toán booking của học sinh tự đăng ký (không có phụ huynh quản lý) được thực hiện.
/// Áp dụng cho từng lần trừ tiền thực tế (cọc buổi đầu, hoặc phần còn lại), không phải tổng
/// giá trị khóa học — một booking nhiều buổi có thể cần OTP ở lần trả thứ hai dù lần đầu không cần.
/// </summary>
public static class LargeTransactionPolicy
{
    public const decimal ThresholdAmount = 2_000_000m;
}
