using Microsoft.EntityFrameworkCore;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;

namespace MV.ApplicationLayer.Helpers;

/// <summary>
/// Resolves the manual-payout audit fields (generated payout code, paid-at time, proof receipt)
/// for a withdrawal. Shared by tutor- and parent-facing services so both surface the same data
/// the same way; callers are responsible for their own ownership check before calling this.
/// </summary>
public static class PayoutProofResolver
{
    public readonly record struct Result(
        string? ProviderTransactionId,
        string? BankTransactionCode,
        DateTime? PaidAt,
        string? ProofImageUrl);

    public static async Task<Result> ResolveAsync(
        IAppDbContext context,
        IFileStorageService fileStorageService,
        int withdrawalId,
        CancellationToken ct = default)
    {
        var payout = await context.PaymentTransactions
            .AsNoTracking()
            .Where(t => t.Withdrawalid == withdrawalId
                        && t.Purpose == PaymentTransactionPurpose.Withdrawal
                        && t.Status == PaymentTransactionStatus.Succeeded)
            .OrderByDescending(t => t.Paymenttransactionid)
            .Select(t => new { t.Providertransactionid, t.Banktransactioncode, t.Paidat, t.Proofimagepath })
            .FirstOrDefaultAsync(ct);

        var proofImageUrl = string.IsNullOrWhiteSpace(payout?.Proofimagepath)
            ? null
            : fileStorageService.GenerateSignedUrl(payout.Proofimagepath);

        return new Result(payout?.Providertransactionid, payout?.Banktransactioncode, payout?.Paidat, proofImageUrl);
    }
}
