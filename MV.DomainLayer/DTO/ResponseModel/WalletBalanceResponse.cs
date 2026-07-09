namespace MV.DomainLayer.DTO.ResponseModel;

public class WalletBalanceResponse
{
    public decimal Balance { get; set; }
    public decimal AvailableBalance { get; set; }
    public decimal FrozenBalance { get; set; }
    public decimal TotalBalance { get; set; }
    public DateTime? LastUpdated { get; set; }
}
