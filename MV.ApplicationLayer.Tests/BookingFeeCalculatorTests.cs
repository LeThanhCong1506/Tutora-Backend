using MV.ApplicationLayer.Helpers;
using Xunit;

namespace MV.ApplicationLayer.Tests;

public class BookingFeeCalculatorTests
{
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

    [Fact]
    public void CalculatePaymentPhases_UsesFirstSessionShareOfParentTotal()
    {
        var fees = BookingFeeCalculator.Calculate(100_000m);

        var (deposit, remaining) = BookingFeeCalculator.CalculatePaymentPhases(
            fees.FinalPrice, totalSessions: 4);

        Assert.Equal(26_250m, deposit);
        Assert.Equal(78_750m, remaining);
        Assert.Equal(fees.FinalPrice, deposit + remaining);
    }
}
