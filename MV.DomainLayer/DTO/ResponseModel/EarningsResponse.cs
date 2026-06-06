namespace MV.DomainLayer.DTO.ResponseModel;

public class EarningsResponse
{
    public List<EarningsItem> Items { get; set; } = new();
}

public class EarningsItem
{
    public string Date { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}
