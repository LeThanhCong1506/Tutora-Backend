namespace MV.DomainLayer.DTO.ResponseModel.Admin;

/// <summary>
/// Admin-only user detail. Family relationships are intentionally kept out of
/// the shared <see cref="UserResponse"/> contract so regular user endpoints do
/// not expose another account's parent/student links.
/// </summary>
public sealed class AdminUserDetailResponse
{
    public UserResponse User { get; set; } = null!;

    public AdminUserRelationshipsResponse Relationships { get; set; } = new();
}

public sealed class AdminUserRelationshipsResponse
{
    /// <summary>The parent linked to a Student account, when one exists.</summary>
    public AdminLinkedUserResponse? Parent { get; set; }

    /// <summary>Student profiles owned by a Parent account.</summary>
    public List<AdminLinkedUserResponse> Students { get; set; } = [];
}

public sealed class AdminLinkedUserResponse
{
    /// <summary>
    /// Related login account id. It may be null for legacy student profiles
    /// that exist under a parent but do not yet have a linked User account.
    /// </summary>
    public string? UserId { get; set; }

    public string FullName { get; set; } = null!;

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public string? AvatarUrl { get; set; }

    public string Role { get; set; } = null!;

    public string? StudentProfileId { get; set; }

    public bool HasAccount { get; set; }
}
