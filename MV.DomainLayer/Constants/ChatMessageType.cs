namespace MV.DomainLayer.Constants;

/// <summary>
/// Chat message type constants. Values must match the `messagetype` column in the `chatmessages` table.
/// </summary>
public static class ChatMessageType
{
    public const string Text            = "text";
    public const string Image           = "image";
    public const string BookingRequest  = "booking_request";
    public const string BookingAccepted = "booking_accepted";
    public const string BookingDeclined = "booking_declined";
    public const string BookingCancelled = "booking_cancelled";
    public const string MeetLink        = "meet_link";
}
