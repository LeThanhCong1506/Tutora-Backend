namespace MV.DomainLayer.Constants;

public static class SqlQueries
{
    public const string LockWalletByUserId = "SELECT * FROM wallets WHERE user_id = {0} FOR UPDATE";
}
