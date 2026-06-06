namespace MV.DomainLayer.Constants;

/// <summary>
/// Zalo Notification Service (ZNS) template type keys.
/// These are the switch-case keys used to resolve ZNS template IDs from configuration.
/// </summary>
public static class ZnsTemplateType
{
    public const string LessonReminder    = "lesson_reminder";
    public const string BookingConfirmed  = "booking_confirmed";
    public const string LessonReport      = "lesson_report";
    public const string PayoutProcessed   = "payout_processed";
}
