using Microsoft.EntityFrameworkCore;
using MV.ApplicationLayer.Interfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.Helpers;

namespace MV.ApplicationLayer.Helpers;

/// <summary>
/// Explains why a <c>status = 0</c> account cannot sign in.
/// </summary>
/// <remarks>
/// The bare "Tài khoản đã bị khóa." told a suspended user nothing they could act on — not what they
/// did, and not whether waiting would help — so every case became a support ticket. Two different
/// mechanisms write that column (a warning-driven suspension and an admin block), and only the
/// suspension has a reason and an end date to offer.
/// </remarks>
public static class AccountLockoutMessage
{
    public const string Generic = "Tài khoản đã bị khóa.";

    public static async Task<string> BuildAsync(
        IAppDbContext db,
        string userId,
        CancellationToken ct = default)
    {
        var suspension = await db.Profilesuspensions
            .AsNoTracking()
            .Where(s => s.Userid == userId && s.Isactive == true)
            .OrderByDescending(s => s.Startdate)
            .Select(s => new { s.Reason, s.Enddate })
            .FirstOrDefaultAsync(ct);

        // No suspension record → an admin block, which carries neither a reason nor an end date.
        if (suspension == null) return Generic;

        var reason = string.IsNullOrWhiteSpace(suspension.Reason)
            ? ""
            : $" Lý do: {suspension.Reason}";

        // An expired end date means the unsuspend job has not swept yet; the account is minutes
        // away from working, so say that rather than quoting a deadline already in the past.
        if (!suspension.Enddate.HasValue)
            return $"Tài khoản của bạn đã bị khóa vĩnh viễn.{reason} Vui lòng liên hệ hỗ trợ nếu bạn cho rằng đây là nhầm lẫn.";

        if (suspension.Enddate.Value <= TimeZoneHelper.UtcNow)
            return $"Tài khoản của bạn vừa hết thời gian đình chỉ và sẽ được mở lại trong ít phút. Vui lòng thử lại sau.{reason}";

        return $"Tài khoản của bạn đang bị tạm đình chỉ đến {FormatVietnamTime(suspension.Enddate.Value)}.{reason}";
    }

    private static string FormatVietnamTime(DateTime utc)
    {
        var vietnamTimeZone = TimeZoneHelper.GetTimeZoneInfo("Asia/Ho_Chi_Minh");
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), vietnamTimeZone);
        return local.ToString("HH:mm dd/MM/yyyy");
    }
}
