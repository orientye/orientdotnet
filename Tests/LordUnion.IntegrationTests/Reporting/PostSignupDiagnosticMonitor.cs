using LordUnion.IntegrationTests.Flows;
using LordUnion.IntegrationTests.Protocol;
using LordUnion.IntegrationTests.Scenarios;
using LordUnion.IntegrationTests.Sessions;

namespace LordUnion.IntegrationTests.Reporting;

public sealed class PostSignupDiagnosticMonitor
{
    private readonly AccountSession session;
    private readonly SignupStageResult signupResult;
    private readonly List<PostSignupMessageEntry> postSignupMessages = new();
    private Action<ProtocolMessage>? previousPushHandler;
    private bool startClientExReceived;
    private uint? startClientExMatchId;
    private string? startClientExIp;
    private uint? startClientExPort;
    private bool startGameClientReceived;
    private uint? startGameClientMatchId;
    private ProtocolMessage? capturedMatchStartMessage;
    private readonly List<ProtocolMessage> capturedMatchProgressMessages = new();

    private PostSignupDiagnosticMonitor(AccountSession session, SignupStageResult signupResult)
    {
        this.session = session;
        this.signupResult = signupResult;
    }

    public string AccountAlias => session.Alias;

    public static IReadOnlyList<PostSignupDiagnosticMonitor> Install(
        IEnumerable<(AccountSession Session, SignupStageResult Result)> signupResults)
    {
        var monitors = new List<PostSignupDiagnosticMonitor>();
        foreach (var (session, signupResult) in signupResults)
        {
            var monitor = new PostSignupDiagnosticMonitor(session, signupResult);
            monitor.Install();
            monitors.Add(monitor);
        }

        return monitors;
    }

    public void SeedFlowState(EnterMatchFlowSessionState flowState)
    {
        ArgumentNullException.ThrowIfNull(flowState);
        if (capturedMatchStartMessage is not null)
        {
            flowState.CaptureFromAnyMessage(capturedMatchStartMessage);
        }

        foreach (var message in capturedMatchProgressMessages)
        {
            flowState.CaptureFromAnyMessage(message);
        }
    }

    public static void SeedFlowStates(
        IReadOnlyList<PostSignupDiagnosticMonitor> monitors,
        IReadOnlyDictionary<string, EnterMatchFlowSessionState> flowStatesByAlias)
    {
        foreach (var monitor in monitors)
        {
            if (flowStatesByAlias.TryGetValue(monitor.AccountAlias, out var flowState))
            {
                monitor.SeedFlowState(flowState);
            }
        }
    }

    public SignupDiagnosticSnapshot CreateSnapshot()
    {
        return new SignupDiagnosticSnapshot
        {
            AccountAlias = session.Alias,
            UserId = session.UserId ?? 0,
            SignupSuccess = signupResult.Success,
            SignupAckParam = signupResult.SignupErrorCode,
            MobileAckParam = signupResult.MobileAckParam,
            SignupFlags = signupResult.Flags,
            TourneyId = signupResult.TourneyId,
            MatchPoint = signupResult.MatchPoint,
            GameId = (int)signupResult.GameId,
            StartClientExReceived = startClientExReceived,
            StartClientExMatchId = startClientExMatchId,
            StartClientExIp = startClientExIp,
            StartClientExPort = startClientExPort,
            StartGameClientReceived = startGameClientReceived,
            StartGameClientMatchId = startGameClientMatchId,
            PostSignupMessages = postSignupMessages.ToList(),
        };
    }

    public void Install()
    {
        previousPushHandler = session.PushMessageReceived;
        session.PushMessageReceived = message =>
        {
            CaptureMessage(message);
            previousPushHandler?.Invoke(message);
        };
    }

    private void CaptureMessage(ProtocolMessage message)
    {
        postSignupMessages.Add(new PostSignupMessageEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Kind = message.Kind.ToString(),
            Header0 = message.Header0,
            MobileParam = message.Param == 0 ? null : message.Param,
            Description = SessionMessageRouter.DescribeMessage(message),
        });

        if (message.Kind == ProtocolMessageKind.StartClientExAck)
        {
            var ack = message.StartClientExAcknowledgement;
            startClientExReceived = true;
            startClientExMatchId = ack?.Matchid;
            startClientExIp = ack?.Ip;
            startClientExPort = ack?.Port;
            capturedMatchStartMessage = message;
        }

        if (message.Kind == ProtocolMessageKind.StartGameClientAck)
        {
            var ack = message.StartGameClientAcknowledgement;
            startGameClientReceived = true;
            startGameClientMatchId = ack?.Matchid;
            capturedMatchStartMessage ??= message;
        }

        if (message.Kind is ProtocolMessageKind.EnterRoundAck
            or ProtocolMessageKind.InitGameTableAck
            or ProtocolMessageKind.AddGamePlayerInfoAck)
        {
            capturedMatchProgressMessages.Add(message);
        }
    }
}

public sealed class PostSignupMessageEntry
{
    public DateTimeOffset Timestamp { get; init; }

    public string Kind { get; init; } = string.Empty;

    public uint Header0 { get; init; }

    public uint? MobileParam { get; init; }

    public string Description { get; init; } = string.Empty;
}

public sealed class SignupDiagnosticSnapshot
{
    public string AccountAlias { get; init; } = string.Empty;

    public uint UserId { get; init; }

    public bool SignupSuccess { get; init; }

    public uint SignupAckParam { get; init; }

    public uint MobileAckParam { get; init; }

    public int SignupFlags { get; init; }

    public uint TourneyId { get; init; }

    public uint MatchPoint { get; init; }

    public int GameId { get; init; }

    public bool StartClientExReceived { get; init; }

    public uint? StartClientExMatchId { get; init; }

    public string? StartClientExIp { get; init; }

    public uint? StartClientExPort { get; init; }

    public bool StartGameClientReceived { get; init; }

    public uint? StartGameClientMatchId { get; init; }

    public IReadOnlyList<PostSignupMessageEntry> PostSignupMessages { get; init; } =
        Array.Empty<PostSignupMessageEntry>();
}

public static class SignupDiagnosticWriter
{
    public static void WriteConsoleSummary(IReadOnlyList<SignupDiagnosticSnapshot> snapshots)
    {
        if (snapshots.Count == 0)
        {
            return;
        }

        Console.WriteLine();
        Console.WriteLine("--- Signup Diagnostics ---");
        foreach (var snapshot in snapshots)
        {
            WriteSnapshot(snapshot);
        }
    }

    private static void WriteSnapshot(SignupDiagnosticSnapshot snapshot)
    {
        Console.WriteLine($"  {snapshot.AccountAlias} (userid={snapshot.UserId}):");
        Console.WriteLine(
            $"    SignupAck.param={snapshot.SignupAckParam} mobile.param={snapshot.MobileAckParam} " +
            $"flags={snapshot.SignupFlags} tourneyId={snapshot.TourneyId} success={snapshot.SignupSuccess}");

        WriteMatchStartLine(
            "StartClientExAck",
            snapshot.StartClientExReceived,
            snapshot.StartClientExMatchId,
            extra: FormatIpPort(snapshot.StartClientExIp, snapshot.StartClientExPort));
        WriteMatchStartLine(
            "StartGameClientAck",
            snapshot.StartGameClientReceived,
            snapshot.StartGameClientMatchId,
            extra: null);

        if (snapshot.PostSignupMessages.Count == 0)
        {
            Console.WriteLine("    Post-signup messages: (none captured on push handler)");
            return;
        }

        Console.WriteLine($"    Post-signup messages ({snapshot.PostSignupMessages.Count}):");
        foreach (var entry in snapshot.PostSignupMessages.TakeLast(20))
        {
            var mobileParam = entry.MobileParam is uint mobileParamValue
                ? $", mobile.param={mobileParamValue}"
                : string.Empty;
            Console.WriteLine(
                $"      {entry.Timestamp:HH:mm:ss.fff} {entry.Kind} header0={entry.Header0}{mobileParam} {entry.Description}");
        }
    }

    private static void WriteMatchStartLine(
        string label,
        bool received,
        uint? matchId,
        string? extra)
    {
        if (!received)
        {
            Console.WriteLine($"    {label}: not received on push handler");
            return;
        }

        var suffix = string.IsNullOrWhiteSpace(extra) ? string.Empty : $", {extra}";
        Console.WriteLine($"    {label}: received on push handler, matchId={matchId}{suffix}");
    }

    private static string? FormatIpPort(string? ip, uint? port)
    {
        if (string.IsNullOrWhiteSpace(ip) && port is null or 0)
        {
            return null;
        }

        return $"ip={ip ?? "(empty)"}, port={port ?? 0}";
    }
}