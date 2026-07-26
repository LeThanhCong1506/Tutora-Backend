namespace MV.DomainLayer.Constants;

/// <summary>
/// Which private admin-mediated conversation a DisputeMessage belongs to. Tutor and
/// parent/student each get their own thread with admin — neither side sees the other's.
/// </summary>
public static class DisputeThreadType
{
    public const string Tutor = "tutor";
    public const string Parent = "parent";

    public static readonly string[] All = { Tutor, Parent };
}
