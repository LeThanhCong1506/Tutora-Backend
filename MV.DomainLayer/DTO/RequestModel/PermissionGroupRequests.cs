using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel;

public class CreatePermissionGroupRequest
{
    [Required, StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = null!;

    [StringLength(255)]
    public string? Description { get; set; }

    [Required]
    public List<string> PermissionKeys { get; set; } = new();
}

public sealed class UpdatePermissionGroupRequest : CreatePermissionGroupRequest
{
    [Required, Range(0, long.MaxValue)]
    public long? ExpectedVersion { get; set; }
}

public sealed class SetStaffPermissionGroupRequest
{
    public Guid? PermissionGroupId { get; set; }

    [Required, Range(0, long.MaxValue)]
    public long? ExpectedVersion { get; set; }
}

public sealed class PermissionGroupListParameters
{
    public string? SearchTerm { get; set; }

    [Range(1, int.MaxValue)]
    public int PageNumber { get; set; } = 1;

    [Range(1, 100)]
    public int PageSize { get; set; } = 20;
}
