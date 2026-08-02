using MV.ApplicationLayer.Helpers;
using Xunit;

namespace MV.ApplicationLayer.Tests;

// Maps to Excel sheet "CalculateBookingFees" (Code_1, BookingFeeCalculator.CalculatePaymentPhases).
public class CalculateBookingFeesTests
{
    [Fact]
    public void MultiSession_EvenDivision_SplitsDepositAndRemaining()
    {
        var (deposit, remaining) = BookingFeeCalculator.CalculatePaymentPhases(1_000_000m, totalSessions: 4);

        Assert.Equal(250_000m, deposit);
        Assert.Equal(750_000m, remaining);
    }

    [Fact]
    public void SingleSession_IsDepositOnly()
    {
        var (deposit, remaining) = BookingFeeCalculator.CalculatePaymentPhases(1_000_000m, totalSessions: 1);

        Assert.Equal(1_000_000m, deposit);
        Assert.Equal(0m, remaining);
    }

    [Fact]
    public void UnevenDivision_FloorsDeposit()
    {
        var (deposit, remaining) = BookingFeeCalculator.CalculatePaymentPhases(999_999m, totalSessions: 4);

        Assert.Equal(249_999m, deposit);
        Assert.Equal(750_000m, remaining);
    }

    [Fact]
    public void Calculate_ChargesFivePercentToEachSide()
    {
        var fees = BookingFeeCalculator.Calculate(100_000m);

        Assert.Equal(100_000m, fees.BaseAmount);
        Assert.Equal(5_000m, fees.ParentFee);
        Assert.Equal(5_000m, fees.TutorFeeCut);
        Assert.Equal(10_000m, fees.PlatformFee);
        Assert.Equal(105_000m, fees.FinalPrice);
        Assert.Equal(95_000m, fees.TutorReceivable);
    }
}
