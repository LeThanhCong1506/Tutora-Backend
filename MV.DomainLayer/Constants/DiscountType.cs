namespace MV.DomainLayer.Constants;

/// <summary>
/// Promotion discount type constants. Values match the `discounttype` column in the `promotions` table.
/// </summary>
public static class DiscountType
{
    public const string Percent = "percent";
    public const string Fixed   = "fixed";
}
