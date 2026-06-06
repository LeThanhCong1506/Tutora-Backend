namespace MV.DomainLayer.DTO.ResponseModel;

public class PromotionValidateResponse
{
    public bool Valid { get; set; }
    public string? Code { get; set; }
    public string? DiscountType { get; set; }
    public decimal? DiscountValue { get; set; }
    public decimal? MaxDiscountAmount { get; set; }
    public decimal? MinOrderValue { get; set; }
    public string? Message { get; set; }
}
