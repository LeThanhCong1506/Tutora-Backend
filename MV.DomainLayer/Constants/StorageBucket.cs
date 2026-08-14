namespace MV.DomainLayer.Constants;

/// <summary>
/// Supabase storage bucket name constants.
/// Values must match the exact bucket names configured in Supabase.
/// </summary>
public static class StorageBucket
{
    /// <summary>Bucket for student/parent avatars.</summary>
    public const string Avatars            = "avatars";

    /// <summary>Bucket for tutor avatars (separate from student/parent avatars).</summary>
    public const string TutorAvatars       = "tutor-avatars";

    /// <summary>Bucket for tutor certificate documents (jpg, jpeg, png, pdf).</summary>
    public const string CertificateFiles   = "certificate-files";

    /// <summary>Bucket for tutor introduction videos.</summary>
    public const string VideoIntroduction  = "video-introduction";

    /// <summary>Bucket for class session attachment files. Value is the actual Supabase bucket name — unaffected by the lessons→class_sessions DB rename.</summary>
    public const string ClassSessionAttachments  = "lesson-attachments";

    /// <summary>Bucket for tutor-uploaded learning materials shared with a booking's student.</summary>
    public const string LearningMaterials  = "learning-materials";

    /// <summary>Bucket for tutor CCCD (citizen ID card) front and back images.</summary>
    public const string CccdFiles          = "cccd-documents";

    /// <summary>Private receipt images for staff/admin manual payout transfers.</summary>
    public const string PayoutProofs       = "payout-proofs";

    /// <summary>Private proof images for admin system-fund top-ups.</summary>
    public const string SystemFundProofs   = "system-fund-proofs";

    /// <summary>Public icons cho gói AI credit.</summary>
    public const string AiCreditIcons      = "ai-credit-icons";
}
