namespace MV.DomainLayer.Constants;

public static class SqlQueries
{
    public const string LockBookingById = "SELECT * FROM bookings WHERE booking_id = {0} FOR UPDATE";
    public const string LockDisputeById = "SELECT * FROM disputes WHERE dispute_id = {0} FOR UPDATE";
    public const string LockClassSessionById = "SELECT * FROM class_sessions WHERE class_session_id = {0} FOR UPDATE";
    public const string LockWalletByUserId = "SELECT * FROM wallets WHERE user_id = {0} FOR UPDATE";
}
