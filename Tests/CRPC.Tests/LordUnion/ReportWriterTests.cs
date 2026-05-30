using System.Text.Json;
using LordUnion.IntegrationTests.Protocol;
using LordUnion.IntegrationTests.Reporting;
using LordUnion.IntegrationTests.Sessions;

namespace CRPC.Tests.LordUnion;

public class ReportWriterTests
{
    private readonly ReportWriter reportWriter = new();

    [Fact]
    public void WriteConsoleSummary_DoesNotThrow_ForSuccessReport()
    {
        var report = CreateSuccessReport();
        var metadata = CreateMetadata();

        var output = CaptureConsoleOutput(() => reportWriter.WriteConsoleSummary(report, metadata));

        Assert.Contains("SUCCESS ThreePlayersOneGame", output, StringComparison.Ordinal);
        Assert.Contains("player1", output, StringComparison.Ordinal);
        Assert.Contains("winSeat=1", output, StringComparison.Ordinal);
        Assert.Contains("gameEnd player1:seat=1/signal=LordResultAck", output, StringComparison.Ordinal);
        Assert.Contains("cleanup player1:completed=True/unsignup=0", output, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteConsoleSummary_UsesCompactSuccessOutput()
    {
        var report = new ScenarioReport
        {
            Success = true,
            MatchId = 475051269,
            TableId = 475051269,
            WinSeat = 1,
            GameEndSummaries =
            [
                new AccountGameEndSummary
                {
                    AccountAlias = "player1",
                    WinSeat = 1,
                    EndSignal = "LordResultAck",
                },
            ],
            AccountTimings =
            [
                new AccountPhaseTiming
                {
                    AccountAlias = "player1",
                    LoginDuration = TimeSpan.FromMilliseconds(416),
                    SignupDuration = TimeSpan.FromMilliseconds(74),
                    EnterMatchDuration = TimeSpan.FromMilliseconds(3440),
                    GameDuration = TimeSpan.FromMilliseconds(146300),
                },
            ],
        };
        var metadata = new ReportMetadata
        {
            ScenarioName = "ThreePlayersOneGame",
            StartedAt = DateTimeOffset.Parse("2026-05-29T03:20:00Z"),
            EndedAt = DateTimeOffset.Parse("2026-05-29T03:22:35Z"),
        };

        var output = CaptureConsoleOutput(() => reportWriter.WriteConsoleSummary(report, metadata));

        Assert.Contains("SUCCESS ThreePlayersOneGame", output, StringComparison.Ordinal);
        Assert.Contains("player1 login=416ms signup=74ms enter=3.44s game=146.30s", output, StringComparison.Ordinal);
        Assert.DoesNotContain("--- Signup Diagnostics ---", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Post-signup messages", output, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteConsoleSummary_DoesNotThrow_ForFailureReport()
    {
        var report = CreateFailureReport();
        var metadata = CreateMetadata();

        var output = CaptureConsoleOutput(() => reportWriter.WriteConsoleSummary(report, metadata));

        Assert.Contains("FAILED", output, StringComparison.Ordinal);
        Assert.Contains("First Failure", output, StringComparison.Ordinal);
        Assert.Contains("login timeout", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WriteJsonReport_CreatesFileWithExpectedFields_ForSuccessReport()
    {
        var report = CreateSuccessReport();
        var metadata = CreateMetadata();
        var outputDirectory = CreateTempOutputDirectory();

        var jsonPath = reportWriter.WriteJsonReport(report, metadata, outputDirectory);

        Assert.True(File.Exists(jsonPath));
        using var document = JsonDocument.Parse(File.ReadAllText(jsonPath));
        var root = document.RootElement;

        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("ThreePlayersOneGame", root.GetProperty("scenarioName").GetString());
        Assert.Equal(900001u, root.GetProperty("matchId").GetUInt32());
        Assert.Equal(900001u, root.GetProperty("tableId").GetUInt32());
        Assert.Equal(1u, root.GetProperty("winSeat").GetUInt32());
        Assert.Equal(3, root.GetProperty("accountTimings").GetArrayLength());
        var cleanup = root.GetProperty("postGameCleanupSummaries");
        Assert.Single(cleanup.EnumerateArray());
        Assert.True(cleanup[0].GetProperty("completed").GetBoolean());
        Assert.Equal(0u, cleanup[0].GetProperty("unsignupParam").GetUInt32());
        if (root.TryGetProperty("firstFailure", out var failure))
        {
            Assert.Equal(JsonValueKind.Null, failure.ValueKind);
        }
    }

    [Fact]
    public void WriteJsonReport_CreatesFileWithExpectedFields_ForFailureReport()
    {
        var report = CreateFailureReport();
        var metadata = CreateMetadata();
        var outputDirectory = CreateTempOutputDirectory();

        var jsonPath = reportWriter.WriteJsonReport(report, metadata, outputDirectory);

        Assert.True(File.Exists(jsonPath));
        using var document = JsonDocument.Parse(File.ReadAllText(jsonPath));
        var root = document.RootElement;

        Assert.False(root.GetProperty("success").GetBoolean());
        var failure = root.GetProperty("firstFailure");
        Assert.Equal("player2", failure.GetProperty("accountAlias").GetString());
        Assert.Equal("Login timeout waiting for LoginAck.", failure.GetProperty("message").GetString());
        Assert.Equal("LoginTimeout", failure.GetProperty("timeoutName").GetString());
    }

    private static ScenarioReport CreateSuccessReport()
    {
        return new ScenarioReport
        {
            Success = true,
            MatchId = 900001,
            TableId = 900001,
            WinSeat = 1,
            GameEndSummaries =
            [
                new AccountGameEndSummary { AccountAlias = "player1", WinSeat = 1, EndSignal = "LordResultAck" },
                new AccountGameEndSummary { AccountAlias = "player2", WinSeat = 1, EndSignal = "LordResultAck" },
                new AccountGameEndSummary { AccountAlias = "player3", WinSeat = 1, EndSignal = "LordResultAck" },
            ],
            SeatUserMapping = new Dictionary<uint, uint>
            {
                [1] = 10001,
                [2] = 10002,
                [3] = 10003,
            },
            PostGameCleanupSummaries =
            [
                new AccountCleanupSummary
                {
                    AccountAlias = "player1",
                    Completed = true,
                    UnsignupSent = true,
                    UnsignupAckReceived = true,
                    UnsignupParam = 0,
                    DiscoveredMatchIds = [900001],
                    ExitGameAttemptedMatchIds = [900001],
                    ExitMatchAttemptedMatchIds = [900001],
                },
            ],
            AccountTimings =
            [
                new AccountPhaseTiming
                {
                    AccountAlias = "player1",
                    ConnectDuration = TimeSpan.FromMilliseconds(120),
                    LoginDuration = TimeSpan.FromMilliseconds(450),
                    SignupDuration = TimeSpan.FromSeconds(2),
                    EnterMatchDuration = TimeSpan.FromSeconds(1),
                    GameDuration = TimeSpan.FromSeconds(30),
                },
                new AccountPhaseTiming
                {
                    AccountAlias = "player2",
                    ConnectDuration = TimeSpan.FromMilliseconds(130),
                    LoginDuration = TimeSpan.FromMilliseconds(460),
                    SignupDuration = TimeSpan.FromSeconds(2),
                    EnterMatchDuration = TimeSpan.FromSeconds(1),
                    GameDuration = TimeSpan.FromSeconds(30),
                },
                new AccountPhaseTiming
                {
                    AccountAlias = "player3",
                    ConnectDuration = TimeSpan.FromMilliseconds(140),
                    LoginDuration = TimeSpan.FromMilliseconds(470),
                    SignupDuration = TimeSpan.FromSeconds(2),
                    EnterMatchDuration = TimeSpan.FromSeconds(1),
                    GameDuration = TimeSpan.FromSeconds(30),
                },
            ],
        };
    }

    private static ScenarioReport CreateFailureReport()
    {
        return new ScenarioReport
        {
            Success = false,
            AccountTimings =
            [
                new AccountPhaseTiming
                {
                    AccountAlias = "player1",
                    ConnectDuration = TimeSpan.FromMilliseconds(120),
                    LoginDuration = TimeSpan.FromMilliseconds(450),
                },
                new AccountPhaseTiming
                {
                    AccountAlias = "player2",
                    ConnectDuration = TimeSpan.FromMilliseconds(130),
                },
            ],
            FirstFailure = new ScenarioFailureDetail
            {
                AccountAlias = "player2",
                SessionState = AccountSessionState.Failed,
                Phase = ProtocolPhase.Login,
                Message = "Login timeout waiting for LoginAck.",
                TimeoutName = "LoginTimeout",
                Exception = new TimeoutException("LoginAck not received."),
            },
        };
    }

    private static ReportMetadata CreateMetadata()
    {
        var startedAt = new DateTimeOffset(2026, 5, 25, 10, 0, 0, TimeSpan.Zero);
        return new ReportMetadata
        {
            StartedAt = startedAt,
            EndedAt = startedAt.AddSeconds(45),
        };
    }

    private static string CaptureConsoleOutput(Action action)
    {
        var originalOut = Console.Out;
        try
        {
            using var writer = new StringWriter();
            Console.SetOut(writer);
            action();
            return writer.ToString();
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    private static string CreateTempOutputDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "lordunion-report-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
