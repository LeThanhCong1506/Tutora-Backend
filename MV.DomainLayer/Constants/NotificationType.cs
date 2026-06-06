namespace MV.DomainLayer.Constants;

/// <summary>
/// Notification type constants used in the `type` column of the notifications table.
/// </summary>
public static class NotificationType
{
    public const string BookingNew      = "booking_new";
    public const string BookingAccepted = "booking_accepted";
    public const string BookingDeclined = "booking_declined";
    public const string PaymentSuccess  = "payment_success";
    public const string LessonReminder  = "lesson_reminder";
    public const string LessonCheckin   = "lesson_checkin";
    public const string LessonReport    = "lesson_report";
    public const string LessonConfirmed = "lesson_confirmed";
    public const string LessonNoShow    = "lesson_no_show";
    public const string Message         = "message";
    public const string Warning         = "warning";
}
