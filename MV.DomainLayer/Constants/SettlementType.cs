namespace MV.DomainLayer.Constants;

/// <summary>
/// Settlement type constants used in API responses only (not stored in DB).
/// </summary>
public static class SettlementType
{
    /// <summary>Settlement triggered automatically by system timeout.</summary>
    public const string AutoConfirm    = "auto_confirm";

    /// <summary>Settlement triggered manually by admin.</summary>
    public const string Manual         = "manual";

    /// <summary>Booking completed: full tutor earnings released.</summary>
    public const string FullRelease    = "full_release";

    /// <summary>Individual lesson confirmed, funds held until booking completion.</summary>
    public const string LessonConfirmed = "lesson_confirmed";

    /// <summary>Full 100% refund to parent.</summary>
    public const string FullRefund     = "full_refund";

    /// <summary>50% refund to parent.</summary>
    public const string Refund50       = "refund_50";
}
