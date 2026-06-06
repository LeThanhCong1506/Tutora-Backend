namespace MV.DomainLayer.Constants;

public static class SqlQueries
{
    public const string LockWalletByUserId = "SELECT * FROM wallets WHERE userid = {0} FOR UPDATE";
}
