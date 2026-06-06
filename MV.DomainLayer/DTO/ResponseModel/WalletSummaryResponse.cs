namespace MV.DomainLayer.DTO.ResponseModel;

public class WalletSummaryResponse
{
    public decimal Balance { get; set; }
    public decimal FrozenBalance { get; set; }
    public DateTime? LastUpdated { get; set; }
}
