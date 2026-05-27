using LordUnion.IntegrationTests.Sessions;

namespace LordUnion.IntegrationTests.Reporting;

public static class MatchStartDiagnostics
{
    public static string BuildMatchStartTimeoutMessage(AccountSession session, string? innerMessage = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        var message = string.IsNullOrWhiteSpace(innerMessage)
            ? $"Timed out waiting for StartGameClientAck or StartClientExAck on account '{session.Alias}' in state {session.State} (phase {session.CurrentPhase})."
            : innerMessage;

        var recent = DescribeRecentIngress(session);
        if (string.IsNullOrWhiteSpace(recent))
        {
            return message;
        }

        return $"{message}{Environment.NewLine}Recent recv:{Environment.NewLine}{recent}";
    }

    public static string DescribeRecentIngress(AccountSession session, int maxEntries = 20)
    {
        ArgumentNullException.ThrowIfNull(session);

        return string.Join(
            Environment.NewLine,
            session.ReceivedMessages
                .TakeLast(maxEntries)
                .Select(entry => $"  {entry.Timestamp:HH:mm:ss.fff} {entry.Kind} {entry.Description}"));
    }
}
