namespace MV.DomainLayer.DTO.ResponseModel;

public sealed class PermissionGroupReferenceResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class PermissionGroupSummaryResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int PermissionCount { get; set; }
    public int TotalPermissionCount { get; set; }
    public int StaffCount { get; set; }
    public long Version { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsActive { get; set; }
}

public sealed class PermissionGroupDetailResponse : PermissionGroupSummaryResponse
{
    public List<string> PermissionKeys { get; set; } = new();
}

public sealed class StaffPermissionGroupResponse
{
    public string StaffId { get; set; } = string.Empty;
    public string? StaffFullName { get; set; }
    public PermissionGroupReferenceResponse? PermissionGroup { get; set; }
    public long AssignmentVersion { get; set; }
    public List<string> PermissionKeys { get; set; } = new();
    public DateTime? UpdatedAt { get; set; }
}

public sealed class AccessMeResponse
{
    public string Role { get; set; } = string.Empty;
    public List<string> PermissionKeys { get; set; } = new();
    public PermissionGroupReferenceResponse? PermissionGroup { get; set; }
    public long? GroupVersion { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
