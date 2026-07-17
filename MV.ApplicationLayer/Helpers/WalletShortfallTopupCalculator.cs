namespace MV.ApplicationLayer.Helpers;

public static class WalletShortfallTopupCalculator
{
    public const decimal MinimumTopupAmount = 10_000m;

    public static decimal Calculate(decimal amountDue, decimal walletBalance)
    {
        if (amountDue <= 0)
            throw new ArgumentOutOfRangeException(nameof(amountDue));

        var shortfall = amountDue - walletBalance;
        if (shortfall <= 0)
            return 0;

        return Math.Max(Math.Ceiling(shortfall), MinimumTopupAmount);
    }
}
