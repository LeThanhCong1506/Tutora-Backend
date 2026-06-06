namespace MV.DomainLayer.Constants;

/// <summary>
/// Dispute type constants. Values match the `disputetype` column in the `disputes` table.
/// </summary>
public static class DisputeTypes
{
    public const string NoShow  = "no_show";
    public const string Quality = "quality";
    public const string Payment = "payment";
    public const string Other   = "other";

    public static readonly string[] All = { NoShow, Quality, Payment, Other };
}
