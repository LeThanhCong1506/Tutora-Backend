using Microsoft.EntityFrameworkCore;
using MV.ApplicationLayer.Interfaces;

namespace MV.ApplicationLayer.Helpers;

public static class PromotionUsageHelper
{
    /// <summary>
    /// Return one promotion usage when a booking is terminated before completion
    /// (cancel / decline / payment-timeout / tutor-response-timeout / no-show change-tutor).
    /// Mirrors the increment done at booking creation (BookingService). Mutates the tracked
    /// Promotion entity only — the caller's SaveChanges flushes it within the caller's transaction.
    /// </summary>
    public static async Task ReturnUsageAsync(IAppDbContext db, int? promotionId, CancellationToken ct = default)
    {
        if (promotionId is null) return;
        var promo = await db.Promotions.FirstOrDefaultAsync(p => p.Promotionid == promotionId.Value, ct);
        if (promo != null)
            promo.Usagecount = Math.Max(0, (promo.Usagecount ?? 0) - 1);
    }
}
