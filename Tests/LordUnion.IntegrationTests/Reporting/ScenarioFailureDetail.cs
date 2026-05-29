using LordUnion.IntegrationTests.Protocol;
using LordUnion.IntegrationTests.Sessions;

namespace LordUnion.IntegrationTests.Reporting;

public sealed class ScenarioFailureDetail
{
    public string AccountAlias { get; init; } = string.Empty;

    public AccountSessionState SessionState { get; init; }

    public ProtocolPhase Phase { get; init; }

    public SessionMessageLogEntry? LastSentMessage { get; init; }

    public SessionMessageLogEntry? LastReceivedMessage { get; init; }

    public IReadOnlyList<SessionMessageLogEntry> ReceivedMessages { get; init; } =
        Array.Empty<SessionMessageLogEntry>();

    public string Message { get; init; } = string.Empty;

    public string? TimeoutName { get; init; }

    public Exception? Exception { get; init; }

    public static ScenarioFailureDetail FromSession(
        AccountSession session,
        string message,
        string? timeoutName = null,
        Exception? exception = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(message);

        return new ScenarioFailureDetail
        {
            AccountAlias = session.Alias,
            SessionState = session.State,
            Phase = session.CurrentPhase,
            LastSentMessage = session.SentMessages.LastOrDefault(),
            LastReceivedMessage = session.ReceivedMessages.LastOrDefault(),
            ReceivedMessages = session.ReceivedMessages.ToList(),
            Message = message,
            TimeoutName = timeoutName,
            Exception = exception,
        };
    }
}