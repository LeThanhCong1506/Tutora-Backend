namespace MV.DomainLayer.Constants;

/// <summary>
/// JWT claim type names used when parsing Supabase access tokens.
/// Mix of standard JWT claim names (email, name) and Supabase custom claims (phone, user_metadata).
/// </summary>
public static class SupabaseJwtClaims
{
    public const string Email        = "email";
    public const string Name         = "name";
    public const string Phone        = "phone";
    public const string UserMetadata = "user_metadata";
}
