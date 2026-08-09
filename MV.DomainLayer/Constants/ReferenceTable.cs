namespace MV.DomainLayer.Constants;

/// <summary>
/// Reference table name constants used in the `referencetable` column of wallet transactions.
/// Values must match the actual PostgreSQL table names (lowercase).
/// </summary>
public static class ReferenceTable
{
    public const string Booking       = "booking";
    public const string Payment       = "payment";
    public const string Topup         = "topup";
    public const string Withdrawal    = "withdrawal";
    public const string TutorProfiles = "tutorprofiles";
    public const string AdminWalletTransfer = "admin_wallet_transfers";
}
