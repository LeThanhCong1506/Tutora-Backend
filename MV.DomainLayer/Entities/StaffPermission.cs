using System;

namespace MV.DomainLayer.Entities;

public partial class StaffPermission
{
    public string Userid { get; set; } = null!;

    public string PermissionKey { get; set; } = null!;

    public string GrantedBy { get; set; } = null!;

    public DateTime GrantedAt { get; set; }

    public virtual User? User { get; set; }
}
