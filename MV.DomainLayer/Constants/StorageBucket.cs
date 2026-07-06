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

    /// <summary>Bucket for tutor CCCD (citizen ID card) front and back images.</summary>
    public const string CccdFiles          = "cccd-documents";
}
