namespace MV.ApplicationLayer.Helpers;

public static class BookingFeeCalculator
{
    private const decimal ParentFeePercent = 0.05m;
    private const decimal TutorFeePercent = 0.05m;

    public static FeeResult Calculate(decimal baseAmount)
    {
        var parentFee = Math.Round(baseAmount * ParentFeePercent, 2);
        var tutorFeeCut = Math.Round(baseAmount * TutorFeePercent, 2);
        var platformFee = parentFee + tutorFeeCut;
        var finalPrice = baseAmount + parentFee;
        var tutorReceivable = baseAmount - tutorFeeCut;

        return new FeeResult(baseAmount, parentFee, tutorFeeCut, platformFee, finalPrice, tutorReceivable);
    }
}

public record FeeResult(
    decimal BaseAmount,
    decimal ParentFee,
    decimal TutorFeeCut,
    decimal PlatformFee,
    decimal FinalPrice,
    decimal TutorReceivable);
