namespace MV.DomainLayer.Constants;

/// <summary>
/// Approval status text constants used in the tutor verification workflow.
/// </summary>
public static class ApprovalStatusText
{
    public const string Approved               = "Approved";
    public const string Rejected               = "Rejected";
    public const string ApprovedPendingProfile = "ApprovedPendingProfile";

    // Reviewer identity
    public const string AdminReviewer          = "admin";

    // Verification notes
    public const string NoteApprovedByAdmin    = "Approved by admin";
}
