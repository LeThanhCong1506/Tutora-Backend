using System;
using System.Collections.Generic;

namespace MV.DomainLayer.Entities;

public partial class Systemconfig
{
    public int Configid { get; set; }

    public string Configkey { get; set; } = null!;

    public string? Configvalue { get; set; }

    public string? Description { get; set; }

    public string? Updatedby { get; set; }

    public DateTime? Updatedat { get; set; }

    public virtual User? UpdatedbyNavigation { get; set; }
}
