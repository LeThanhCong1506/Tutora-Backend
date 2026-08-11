namespace MV.DomainLayer.DTO.ResponseModel;

public class BankAccountAuditLogResponse
{
    /// <summary>"created" | "updated" | "deleted" — xem <see cref="MV.DomainLayer.Constants.BankAccountAuditAction"/>.</summary>
    public string Action { get; set; } = string.Empty;
    public string? OldBankName { get; set; }
    public string? OldAccountNumber { get; set; }
    public string? OldAccountHolderName { get; set; }
    public string? NewBankName { get; set; }
    public string? NewAccountNumber { get; set; }
    public string? NewAccountHolderName { get; set; }
    public DateTime ChangedAt { get; set; }
}
