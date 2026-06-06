using System;

namespace MV.DomainLayer.Entities;

public partial class Systemalert
{
    public int Alertid { get; set; }

    public string Type { get; set; } = null!;

    public string Severity { get; set; } = null!;

    public string Message { get; set; } = null!;

    public string? Metadata { get; set; }

    public bool Resolved { get; set; }

    public DateTime? Resolvedat { get; set; }

    public string? Resolvedby { get; set; }

    public DateTime Createdat { get; set; }
}
