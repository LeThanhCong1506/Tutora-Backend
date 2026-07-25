using System;

namespace MV.DomainLayer.Entities;

/// <summary>Gói AI credit
public partial class AiCreditPackage
{
    public int Packageid { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    /// <summary>Số lượt AI được cấp — admin chỉnh được.</summary>
    public int Creditamount { get; set; }

    public decimal Price { get; set; }

    public string Currency { get; set; } = null!;

    public bool Ispurchasable { get; set; }

    public bool Isactive { get; set; }

    public int Sortorder { get; set; }

    public string? Description { get; set; }

    public string? Iconurl { get; set; }

    public DateTime Createdat { get; set; }

    public DateTime Updatedat { get; set; }
}
