using MV.DomainLayer.Helpers;

namespace MV.ApplicationLayer.Helpers;

/// <summary>
/// Generates the internal payout reference stored in payment_transactions.provider_transaction_id
/// when staff/admin manually confirm a withdrawal transfer. This is NOT a bank-provided trace code —
/// staff never type it in; the backend mints it at approval time.
/// </summary>
public static class PayoutCodeGenerator
{
    public static string Generate(int withdrawalId)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        return $"WD-{TimeZoneHelper.UtcNow:yyyyMMddHHmmss}-{withdrawalId}-{suffix}";
    }
}
