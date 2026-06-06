namespace MV.ApplicationLayer.Helpers;

public static class OrderCodeHelper
{
    private const long BookingPrefix = 1;
    private const long RemainingPrefix = 2;
    private const long TopupPrefix = 9;
    private const long MinValidOrderCode = 1000000000;

    public static long GenerateBookingOrderCode(int bookingId)
    {
        var random = Random.Shared.Next(1000, 9999);
        return BookingPrefix * 100000000000L + (long)bookingId * 10000L + random;
    }

    public static long GenerateTopupOrderCode(string userId)
    {
        var userHash = Math.Abs(userId.GetHashCode()) % 10000;
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() % 1000000;
        var random = Random.Shared.Next(1000, 9999);
        return TopupPrefix * 10000000000L + userHash * 1000000L + timestamp % 1000 * 1000L + random;
    }

    public static bool IsBookingOrderCode(long orderCode)
    {
        if (orderCode < MinValidOrderCode) return false;
        return orderCode / 100000000000L == BookingPrefix;
    }

    public static long GenerateRemainingOrderCode(int bookingId)
    {
        var random = Random.Shared.Next(1000, 9999);
        return RemainingPrefix * 100000000000L + (long)bookingId * 10000L + random;
    }

    public static bool IsRemainingOrderCode(long orderCode)
    {
        if (orderCode < MinValidOrderCode) return false;
        return orderCode / 100000000000L == RemainingPrefix;
    }

    public static bool IsTopupOrderCode(long orderCode)
    {
        if (orderCode < MinValidOrderCode) return false;
        return orderCode / 10000000000L == TopupPrefix;
    }

    public static int ExtractBookingId(long orderCode)
    {
        if (!IsBookingOrderCode(orderCode) && !IsRemainingOrderCode(orderCode))
            throw new InvalidOperationException("Không phải mã đặt chỗ hoặc mã đơn hàng còn lại.");
        return (int)((orderCode % 100000000000L) / 10000L);
    }
}
