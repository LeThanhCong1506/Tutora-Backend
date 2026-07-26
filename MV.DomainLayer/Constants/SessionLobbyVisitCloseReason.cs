namespace MV.DomainLayer.Constants;

/// <summary>Why an authenticated lobby visit stopped receiving state refreshes.</summary>
public static class SessionLobbyVisitCloseReason
{
    public const string Leave = "leave";
    public const string Disconnect = "disconnect";
}
