namespace MV.DomainLayer.DTO.ResponseModel;

public class TutorBankInfoResponse
{
    public string? BankName { get; set; }
    public string? AccountNumber { get; set; }
    public string? AccountHolderName { get; set; }
    public bool IsVerified { get; set; }
    public DateTime? BankChangedAt { get; set; }
}
