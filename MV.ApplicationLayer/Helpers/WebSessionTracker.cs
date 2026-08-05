using Microsoft.Extensions.Caching.Distributed;

namespace MV.ApplicationLayer.Helpers;

/// <summary>
/// Theo dõi ĐÚNG 1 refresh-token-id đang là "phiên web hiện tại" của mỗi user, lưu trong Redis
/// (key theo Userid, TTL khớp hạn refresh token). Login web mới revoke đúng token đang được trỏ
/// tới rồi ghi đè bằng id mới. App Flutter (mobile) không bao giờ đọc hay ghi key này — cơ chế
/// này không có khái niệm "token nào là mobile", nên không hề kiểm tra/động tới phiên mobile.
/// </summary>
public static class WebSessionTracker
{
    private static string Key(string userId) => $"web-session:{userId}";

    public static Task<string?> GetCurrentAsync(IDistributedCache cache, string userId, CancellationToken ct = default)
        => cache.GetStringAsync(Key(userId), ct);

    public static Task SetAsync(IDistributedCache cache, string userId, string tokenId, TimeSpan ttl, CancellationToken ct = default)
        => cache.SetStringAsync(
            Key(userId),
            tokenId,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl },
            ct);
}
