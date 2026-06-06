using System;

namespace MV.DomainLayer.Entities;

public partial class RefreshToken
{
    public string Id { get; set; } = null!;

    public string Tokenhash { get; set; } = null!;

    public string Userid { get; set; } = null!;

    public string Tokenfamily { get; set; } = null!;

    public DateTime Expiresat { get; set; }

    public DateTime Createdat { get; set; }

    public DateTime? Revokedat { get; set; }

    public string? Replacedbytokenhash { get; set; }

    public virtual User User { get; set; } = null!;
}
