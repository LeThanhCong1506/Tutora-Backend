using MV.ApplicationLayer.Helpers;
using Xunit;

namespace MV.ApplicationLayer.Tests;

public class WalletShortfallTopupCalculatorTests
{
    [Theory]
    [InlineData(100000, 40000, 60000)]
    [InlineData(100000, 95000, 10000)]
    [InlineData(100000, 100000, 0)]
    [InlineData(100000, 120000, 0)]
    [InlineData(100000.5, 40000, 60001)]
    public void Calculate_ReturnsServerAuthoritativeRoundedShortfall(
        decimal amountDue,
        decimal walletBalance,
        decimal expected)
    {
        Assert.Equal(
            expected,
            WalletShortfallTopupCalculator.Calculate(amountDue, walletBalance));
    }

    [Fact]
    public void Calculate_RejectsNonPositiveBookingAmount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WalletShortfallTopupCalculator.Calculate(0, 0));
    }
}
