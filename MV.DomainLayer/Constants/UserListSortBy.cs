namespace MV.DomainLayer.Constants;

/// <summary>
/// User list sort-by option constants. Used by UserRepository paged list query.
/// </summary>
public static class UserListSortBy
{
    public const string FullName = "fullname";
    public const string Email = "email";
    public const string CreatedAt = "createdat";
    public const string CreatedAtAsc = "createdat_asc";
    public const string FullNameAsc  = "fullname_asc";
    public const string FullNameDesc = "fullname_desc";
}
