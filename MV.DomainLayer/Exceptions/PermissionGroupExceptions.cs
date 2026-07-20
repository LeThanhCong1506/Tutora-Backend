namespace MV.DomainLayer.Exceptions;

public sealed class PermissionVersionConflictException : Exception
{
    public PermissionVersionConflictException(string message, long currentVersion)
        : base(message)
    {
        CurrentVersion = currentVersion;
    }

    public long CurrentVersion { get; }
}

public sealed class PermissionGroupInUseException : Exception
{
    public PermissionGroupInUseException(Guid permissionGroupId, int assignedStaffCount)
        : base($"Nhóm quyền đang được gán cho {assignedStaffCount} Staff và không thể xóa.")
    {
        PermissionGroupId = permissionGroupId;
        AssignedStaffCount = assignedStaffCount;
    }

    public Guid PermissionGroupId { get; }
    public int AssignedStaffCount { get; }
}
