namespace MV.DomainLayer.Constants;

/// <summary>
/// Which side of a SupportThread sent a SupportMessage. "Admin" covers any Admin/Staff member —
/// the recipient sees a single unified "Tutora" voice regardless of which staff replied.
/// </summary>
public static class SupportSenderSide
{
    public const string Admin = "admin";
    public const string User = "user";
}
