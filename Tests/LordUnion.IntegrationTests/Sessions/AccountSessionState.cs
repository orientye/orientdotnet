namespace LordUnion.IntegrationTests.Sessions;

public enum AccountSessionState
{
    Disconnected,
    Connecting,
    Connected,
    LoggedIn,
    SignedUp,
    WaitingForMatch,
    EnteringMatch,
    InGame,
    Finished,
    Failed,
}