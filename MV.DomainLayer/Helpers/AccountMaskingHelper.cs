using MV.DomainLayer.Settings;

namespace MV.DomainLayer.Helpers;

public static class AccountMaskingHelper
{
    public static string MaskAccountNumber(string? accountNumber, BankVerificationSettings settings) =>
        !settings.MaskSensitiveData || string.IsNullOrEmpty(accountNumber) || accountNumber.Length <= settings.AccountNumberVisibleDigits
            ? accountNumber ?? string.Empty
            : new string('*', accountNumber.Length - settings.AccountNumberVisibleDigits) + accountNumber[^settings.AccountNumberVisibleDigits..];
}
