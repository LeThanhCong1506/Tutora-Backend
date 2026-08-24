namespace MV.ApplicationLayer.Helpers;

public static class BookingFeeCalculator
{
    /// <summary>
    /// parentFeePercent/tutorFeePercent là phân số (0.05 = 5%), lấy từ
    /// ICommissionConfigService.GetFeePercentsAsync — admin chỉnh qua CMS, không còn hardcode ở
    /// đây. Calculator giữ nguyên là hàm thuần (không tự đọc DB) để dễ test/tái sử dụng.
    /// </summary>
    public static FeeResult Calculate(decimal baseAmount, decimal parentFeePercent, decimal tutorFeePercent)
    {
        var parentFee = Math.Round(baseAmount * parentFeePercent, 2);
        var tutorFeeCut = Math.Round(baseAmount * tutorFeePercent, 2);
        var platformFee = parentFee + tutorFeeCut;
        var finalPrice = baseAmount + parentFee;
        var tutorReceivable = baseAmount - tutorFeeCut;

        return new FeeResult(baseAmount, parentFee, tutorFeeCut, platformFee, finalPrice, tutorReceivable);
    }

    /// <summary>
    /// Tính Deposit và Remaining từ FinalPrice và số buổi.
    /// Deposit = giá 1 buổi (Floor), Remaining = phần còn lại.
    /// Tổng Deposit + Remaining luôn = FinalPrice.
    /// Booking 1 buổi: Deposit = FinalPrice, Remaining = 0.
    /// </summary>
    public static (decimal Deposit, decimal Remaining) CalculatePaymentPhases(
        decimal finalPrice, int totalSessions)
    {
        if (totalSessions <= 1)
            return (finalPrice, 0m);

        var deposit = Math.Floor(finalPrice / totalSessions);
        return (deposit, finalPrice - deposit);
    }
}

public record FeeResult(
    decimal BaseAmount,
    decimal ParentFee,
    decimal TutorFeeCut,
    decimal PlatformFee,
    decimal FinalPrice,
    decimal TutorReceivable);
