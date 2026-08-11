namespace MV.DomainLayer.Constants;

/// <summary>Values must match the `action` CHECK constraint on `bank_account_audit_logs`.</summary>
public static class BankAccountAuditAction
{
    public const string Created = "created";
    public const string Updated = "updated";
    public const string Deleted = "deleted";
}
