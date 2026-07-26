namespace MV.DomainLayer.Constants;

public static class AgoraChannelEventType
{
    public const short ChannelCreate = 101;
    public const short ChannelDestroy = 102;
    public const short BroadcasterJoin = 103;
    public const short BroadcasterLeave = 104;
    public const short UserJoin = 107;
    public const short UserLeave = 108;

    public static bool IsJoin(short eventType)
        => eventType is BroadcasterJoin or UserJoin;

    public static bool IsLeave(short eventType)
        => eventType is BroadcasterLeave or UserLeave;
}
