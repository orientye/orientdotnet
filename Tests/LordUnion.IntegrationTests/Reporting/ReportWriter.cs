using System.Text.Json;
using System.Text.Json.Serialization;

namespace LordUnion.IntegrationTests.Reporting;

public sealed class ReportMetadata
{
    public string ScenarioName { get; init; } = "ThreePlayersOneGame";

    public DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset EndedAt { get; init; }
}

public sealed class ReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public void WriteConsoleSummary(ScenarioReport report, ReportMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(metadata);

        if (report.Success)
        {
            WriteCompactSuccessSummary(report, metadata);
            return;
        }

        Console.WriteLine();
        Console.WriteLine("=== Scenario Report ===");
        Console.WriteLine($"Scenario: {metadata.ScenarioName}");
        Console.WriteLine($"Started:  {metadata.StartedAt:O}");
        Console.WriteLine($"Ended:    {metadata.EndedAt:O}");
        Console.WriteLine($"Duration: {FormatDuration(metadata.EndedAt - metadata.StartedAt)}");
        Console.WriteLine();

        Console.WriteLine("--- Result ---");
        Console.WriteLine(report.Success ? "SUCCESS" : "FAILED");
        Console.WriteLine();

        Console.WriteLine("--- Account Timings ---");
        if (report.AccountTimings.Count == 0)
        {
            Console.WriteLine("(none)");
        }
        else
        {
            foreach (var timing in report.AccountTimings)
            {
                Console.WriteLine($"  {timing.AccountAlias}:");
                Console.WriteLine($"    Connect:     {FormatDuration(timing.ConnectDuration)}");
                Console.WriteLine($"    Login:       {FormatDuration(timing.LoginDuration)}");
                Console.WriteLine($"    Signup:      {FormatDuration(timing.SignupDuration)}");
                Console.WriteLine($"    EnterMatch:  {FormatDuration(timing.EnterMatchDuration)}");
                Console.WriteLine($"    Game:        {FormatDuration(timing.GameDuration)}");
                Console.WriteLine($"    Total:       {FormatDuration(timing.TotalDuration)}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("--- Match / Table / Seats ---");
        Console.WriteLine($"MatchId: {(report.MatchId?.ToString() ?? "(unknown)")}");
        Console.WriteLine($"TableId: {(report.TableId?.ToString() ?? "(unknown)")}");
        if (report.SeatUserMapping is { Count: > 0 })
        {
            foreach (var (seat, userId) in report.SeatUserMapping.OrderBy(pair => pair.Key))
            {
                Console.WriteLine($"  Seat {seat} -> User {userId}");
            }
        }
        else
        {
            Console.WriteLine("Seat mapping: (unknown)");
        }

        Console.WriteLine();
        Console.WriteLine("--- Game Result ---");
        Console.WriteLine($"WinSeat: {(report.WinSeat?.ToString() ?? "(unknown)")}");

        if (report.SignupDiagnostics.Count > 0)
        {
            SignupDiagnosticWriter.WriteConsoleSummary(report.SignupDiagnostics);
        }

        if (report.FirstFailure is not null)
        {
            WriteFailureSection(report.FirstFailure);
        }
    }

    public string WriteJsonReport(ScenarioReport report, ReportMetadata metadata, string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        Directory.CreateDirectory(outputDirectory);

        var fileName = $"scenario-report-{metadata.EndedAt.UtcDateTime:yyyyMMdd'T'HHmmss'Z'}.json";
        var path = Path.Combine(outputDirectory, fileName);
        var dto = ToJsonDto(report, metadata);
        File.WriteAllText(path, JsonSerializer.Serialize(dto, JsonOptions));
        return path;
    }

    private static void WriteCompactSuccessSummary(ScenarioReport report, ReportMetadata metadata)
    {
        Console.WriteLine(
            $"SUCCESS {metadata.ScenarioName} duration={FormatDuration(metadata.EndedAt - metadata.StartedAt)} " +
            $"matchId={report.MatchId?.ToString() ?? "(unknown)"} " +
            $"tableId={report.TableId?.ToString() ?? "(unknown)"} " +
            $"winSeat={FormatWinSeat(report.WinSeat)}");

        if (report.GameEndSummaries.Count > 0)
        {
            Console.WriteLine($"gameEnd {FormatGameEndSummaries(report.GameEndSummaries)}");
        }

        if (report.PostGameCleanupSummaries.Count > 0)
        {
            Console.WriteLine($"cleanup {FormatCleanupSummaries(report.PostGameCleanupSummaries)}");
        }

        foreach (var timing in report.AccountTimings)
        {
            Console.WriteLine(
                $"{timing.AccountAlias} " +
                $"login={FormatDuration(timing.LoginDuration)} " +
                $"signup={FormatDuration(timing.SignupDuration)} " +
                $"enter={FormatDuration(timing.EnterMatchDuration)} " +
                $"game={FormatDuration(timing.GameDuration)}");
        }
    }

    private static void WriteFailureSection(ScenarioFailureDetail failure)
    {
        Console.WriteLine();
        Console.WriteLine("--- First Failure ---");
        Console.WriteLine($"Account:   {failure.AccountAlias}");
        Console.WriteLine($"State:     {failure.SessionState}");
        Console.WriteLine($"Phase:     {failure.Phase}");
        Console.WriteLine($"Message:   {failure.Message}");

        if (!string.IsNullOrWhiteSpace(failure.TimeoutName))
        {
            Console.WriteLine($"Timeout:   {failure.TimeoutName}");
        }

        if (failure.Exception is not null)
        {
            Console.WriteLine($"Exception: {failure.Exception.GetType().Name}: {failure.Exception.Message}");
        }

        WriteMessageLogLine("Last sent", failure.LastSentMessage);
        WriteMessageLogLine("Last recv", failure.LastReceivedMessage);

        if (failure.ReceivedMessages.Count > 0)
        {
            Console.WriteLine("Recv trail (last 15):");
            foreach (var entry in failure.ReceivedMessages.TakeLast(15))
            {
                Console.WriteLine($"  {entry.Timestamp:HH:mm:ss.fff} {entry.Kind} {entry.Description}");
            }
        }
    }

    private static void WriteMessageLogLine(string label, Sessions.SessionMessageLogEntry? entry)
    {
        if (entry is null)
        {
            Console.WriteLine($"{label}:     (none)");
            return;
        }

        Console.WriteLine(
            $"{label}:     {entry.Kind} phase={entry.Phase} {entry.Description}");
    }

    private static ScenarioReportJson ToJsonDto(ScenarioReport report, ReportMetadata metadata)
    {
        return new ScenarioReportJson
        {
            ScenarioName = metadata.ScenarioName,
            StartedAt = metadata.StartedAt,
            EndedAt = metadata.EndedAt,
            DurationMs = (metadata.EndedAt - metadata.StartedAt).TotalMilliseconds,
            Success = report.Success,
            MatchId = report.MatchId,
            TableId = report.TableId,
            WinSeat = report.WinSeat,
            GameEndSummaries = report.GameEndSummaries
                .Select(summary => new AccountGameEndSummaryJson
                {
                    AccountAlias = summary.AccountAlias,
                    WinSeat = summary.WinSeat,
                    EndSignal = summary.EndSignal,
                })
                .ToList(),
            SeatUserMapping = report.SeatUserMapping is null
                ? null
                : report.SeatUserMapping.ToDictionary(pair => pair.Key, pair => pair.Value),
            AccountTimings = report.AccountTimings
                .Select(timing => new AccountPhaseTimingJson
                {
                    AccountAlias = timing.AccountAlias,
                    ConnectDurationMs = timing.ConnectDuration.TotalMilliseconds,
                    LoginDurationMs = timing.LoginDuration.TotalMilliseconds,
                    SignupDurationMs = timing.SignupDuration.TotalMilliseconds,
                    EnterMatchDurationMs = timing.EnterMatchDuration.TotalMilliseconds,
                    GameDurationMs = timing.GameDuration.TotalMilliseconds,
                    TotalDurationMs = timing.TotalDuration.TotalMilliseconds,
                })
                .ToList(),
            PostGameCleanupSummaries = report.PostGameCleanupSummaries
                .Select(summary => new AccountCleanupSummaryJson
                {
                    AccountAlias = summary.AccountAlias,
                    Completed = summary.Completed,
                    UnsignupSent = summary.UnsignupSent,
                    UnsignupAckReceived = summary.UnsignupAckReceived,
                    UnsignupParam = summary.UnsignupParam,
                    DiscoveredMatchIds = summary.DiscoveredMatchIds.ToList(),
                    ExitGameAttemptedMatchIds = summary.ExitGameAttemptedMatchIds.ToList(),
                    ExitMatchAttemptedMatchIds = summary.ExitMatchAttemptedMatchIds.ToList(),
                    ErrorMessage = summary.ErrorMessage,
                })
                .ToList(),
            SignupDiagnostics = report.SignupDiagnostics
                .Select(snapshot => new SignupDiagnosticSnapshotJson
                {
                    AccountAlias = snapshot.AccountAlias,
                    UserId = snapshot.UserId,
                    SignupSuccess = snapshot.SignupSuccess,
                    SignupAckParam = snapshot.SignupAckParam,
                    MobileAckParam = snapshot.MobileAckParam,
                    SignupFlags = snapshot.SignupFlags,
                    TourneyId = snapshot.TourneyId,
                    MatchPoint = snapshot.MatchPoint,
                    GameId = snapshot.GameId,
                    StartClientExReceived = snapshot.StartClientExReceived,
                    StartClientExMatchId = snapshot.StartClientExMatchId,
                    StartClientExIp = snapshot.StartClientExIp,
                    StartClientExPort = snapshot.StartClientExPort,
                    StartGameClientReceived = snapshot.StartGameClientReceived,
                    StartGameClientMatchId = snapshot.StartGameClientMatchId,
                    PostSignupMessages = snapshot.PostSignupMessages
                        .Select(message => new PostSignupMessageEntryJson
                        {
                            Timestamp = message.Timestamp,
                            Kind = message.Kind,
                            Header0 = message.Header0,
                            MobileParam = message.MobileParam,
                            Description = message.Description,
                        })
                        .ToList(),
                })
                .ToList(),
            FirstFailure = report.FirstFailure is null ? null : ToFailureJson(report.FirstFailure),
        };
    }

    private static ScenarioFailureDetailJson ToFailureJson(ScenarioFailureDetail failure)
    {
        return new ScenarioFailureDetailJson
        {
            AccountAlias = failure.AccountAlias,
            SessionState = failure.SessionState,
            Phase = failure.Phase,
            Message = failure.Message,
            TimeoutName = failure.TimeoutName,
            ExceptionType = failure.Exception?.GetType().FullName,
            ExceptionMessage = failure.Exception?.Message,
            LastSentMessage = failure.LastSentMessage is null ? null : ToMessageJson(failure.LastSentMessage),
            LastReceivedMessage =
                failure.LastReceivedMessage is null ? null : ToMessageJson(failure.LastReceivedMessage),
        };
    }

    private static SessionMessageLogEntryJson ToMessageJson(Sessions.SessionMessageLogEntry entry)
    {
        return new SessionMessageLogEntryJson
        {
            Direction = entry.Direction,
            AccountAlias = entry.AccountAlias,
            State = entry.State,
            Phase = entry.Phase,
            Kind = entry.Kind,
            Header0 = entry.Header0,
            Param = entry.Param,
            UserId = entry.UserId,
            Timestamp = entry.Timestamp,
            Description = entry.Description,
        };
    }

    private static string FormatWinSeat(uint? winSeat) =>
        winSeat.HasValue ? winSeat.Value.ToString() : "(unknown)";

    private static string FormatGameEndSummaries(IReadOnlyList<AccountGameEndSummary> summaries) =>
        string.Join(
            " ",
            summaries.Select(summary =>
                $"{summary.AccountAlias}:seat={FormatWinSeat(summary.WinSeat)}/signal={summary.EndSignal ?? "(unknown)"}"));

    private static string FormatCleanupSummaries(IReadOnlyList<AccountCleanupSummary> summaries) =>
        string.Join(
            " ",
            summaries.Select(summary =>
                $"{summary.AccountAlias}:completed={summary.Completed}/unsignup={FormatOptionalUInt(summary.UnsignupParam)}"));

    private static string FormatOptionalUInt(uint? value) =>
        value.HasValue ? value.Value.ToString() : "(unknown)";

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return duration.ToString(@"hh\:mm\:ss\.fff");
        }

        return duration.TotalSeconds >= 1
            ? $"{duration.TotalSeconds:F2}s"
            : $"{duration.TotalMilliseconds:F0}ms";
    }

    private sealed class ScenarioReportJson
    {
        public string ScenarioName { get; init; } = string.Empty;

        public DateTimeOffset StartedAt { get; init; }

        public DateTimeOffset EndedAt { get; init; }

        public double DurationMs { get; init; }

        public bool Success { get; init; }

        public uint? MatchId { get; init; }

        public uint? TableId { get; init; }

        public uint? WinSeat { get; init; }

        public List<AccountGameEndSummaryJson> GameEndSummaries { get; init; } = [];

        public Dictionary<uint, uint>? SeatUserMapping { get; init; }

        public List<AccountPhaseTimingJson> AccountTimings { get; init; } = [];

        public List<SignupDiagnosticSnapshotJson> SignupDiagnostics { get; init; } = [];

        public List<AccountCleanupSummaryJson> PostGameCleanupSummaries { get; init; } = [];

        public ScenarioFailureDetailJson? FirstFailure { get; init; }
    }

    private sealed class AccountCleanupSummaryJson
    {
        public string AccountAlias { get; init; } = string.Empty;

        public bool Completed { get; init; }

        public bool UnsignupSent { get; init; }

        public bool UnsignupAckReceived { get; init; }

        public uint? UnsignupParam { get; init; }

        public List<uint> DiscoveredMatchIds { get; init; } = [];

        public List<uint> ExitGameAttemptedMatchIds { get; init; } = [];

        public List<uint> ExitMatchAttemptedMatchIds { get; init; } = [];

        public string? ErrorMessage { get; init; }
    }

    private sealed class SignupDiagnosticSnapshotJson
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

        public List<PostSignupMessageEntryJson> PostSignupMessages { get; init; } = [];
    }

    private sealed class PostSignupMessageEntryJson
    {
        public DateTimeOffset Timestamp { get; init; }

        public string Kind { get; init; } = string.Empty;

        public uint Header0 { get; init; }

        public uint? MobileParam { get; init; }

        public string Description { get; init; } = string.Empty;
    }

    private sealed class AccountGameEndSummaryJson
    {
        public string AccountAlias { get; init; } = string.Empty;

        public uint? WinSeat { get; init; }

        public string? EndSignal { get; init; }
    }

    private sealed class AccountPhaseTimingJson
    {
        public string AccountAlias { get; init; } = string.Empty;

        public double ConnectDurationMs { get; init; }

        public double LoginDurationMs { get; init; }

        public double SignupDurationMs { get; init; }

        public double EnterMatchDurationMs { get; init; }

        public double GameDurationMs { get; init; }

        public double TotalDurationMs { get; init; }
    }

    private sealed class ScenarioFailureDetailJson
    {
        public string AccountAlias { get; init; } = string.Empty;

        public Sessions.AccountSessionState SessionState { get; init; }

        public Protocol.ProtocolPhase Phase { get; init; }

        public string Message { get; init; } = string.Empty;

        public string? TimeoutName { get; init; }

        public string? ExceptionType { get; init; }

        public string? ExceptionMessage { get; init; }

        public SessionMessageLogEntryJson? LastSentMessage { get; init; }

        public SessionMessageLogEntryJson? LastReceivedMessage { get; init; }
    }

    private sealed class SessionMessageLogEntryJson
    {
        public Sessions.SessionMessageDirection Direction { get; init; }

        public string AccountAlias { get; init; } = string.Empty;

        public Sessions.AccountSessionState State { get; init; }

        public Protocol.ProtocolPhase Phase { get; init; }

        public Protocol.ProtocolMessageKind Kind { get; init; }

        public uint Header0 { get; init; }

        public uint? Param { get; init; }

        public uint? UserId { get; init; }

        public DateTimeOffset Timestamp { get; init; }

        public string Description { get; init; } = string.Empty;
    }
}