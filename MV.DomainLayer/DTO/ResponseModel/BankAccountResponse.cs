namespace MV.DomainLayer.DTO.ResponseModel;

public class BankAccountResponse
{
    public string? BankName { get; set; }
    public string? AccountNumber { get; set; }
    public string? AccountHolderName { get; set; }
    public DateTime? BankChangedAt { get; set; }
}
