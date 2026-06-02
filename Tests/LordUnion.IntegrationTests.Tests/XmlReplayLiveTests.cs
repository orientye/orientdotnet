using LordUnion.IntegrationTests.Config;
using LordUnion.IntegrationTests.Reporting;
using LordUnion.IntegrationTests.Scenarios;

namespace LordUnion.IntegrationTests.Tests;

public sealed class XmlReplayLiveTests
{
    [Fact]
    [Trait("Category", "Live")]
    public void ThreePlayers_XmlReplay_CompletesAgainstCasesServer()
    {
        if (!IsLiveEnabled())
        {
            return;
        }

        var configPath = ResolveLiveConfigPath();
        Assert.True(File.Exists(configPath), $"Live config not found: {configPath}");

        var config = LordUnionTestConfigLoader.Load(configPath);
        ApplyEnvironmentOverrides(config);

        var validationErrors = LordUnionTestConfigValidator.Validate(config);
        Assert.True(
            validationErrors.Count == 0,
            "Config validation failed:\n" + string.Join('\n', validationErrors));

        var report = ThreePlayersOneGameScenario.RunHosted(
            config,
            new ScenarioRunOptions
            {
                UseLiveTransport = true,
                SkipBotPacing = true,
            });

        Assert.True(report.Success, FormatFailure(report));
    }

    private static bool IsLiveEnabled() =>
        string.Equals(Environment.GetEnvironmentVariable("LORDUNION_LIVE"), "1", StringComparison.Ordinal);

    private static string ResolveLiveConfigPath()
    {
        var explicitPath = Environment.GetEnvironmentVariable("LORDUNION_CONFIG");
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return Path.GetFullPath(explicitPath);
        }

        var repoRoot = FindRepoRoot();
        return Path.Combine(repoRoot, "Tests", "LordUnion.IntegrationTests", "appsettings.local.json");
    }

    private static void ApplyEnvironmentOverrides(LordUnionTestConfig config)
    {
        var host = Environment.GetEnvironmentVariable("LORDUNION_HOST");
        if (!string.IsNullOrWhiteSpace(host))
        {
            config.Server.Host = host;
        }

        var portText = Environment.GetEnvironmentVariable("LORDUNION_PORT");
        if (!string.IsNullOrWhiteSpace(portText)
            && int.TryParse(portText, out var port))
        {
            config.Server.Port = port;
        }

        var appIdText = Environment.GetEnvironmentVariable("LORDUNION_APP_ID");
        if (!string.IsNullOrWhiteSpace(appIdText)
            && uint.TryParse(appIdText, out var appId))
        {
            config.Protocol.AppId = appId;
        }
    }

    private static string FormatFailure(ScenarioReport report)
    {
        var failure = report.FirstFailure;
        if (failure is null)
        {
            return "Scenario failed without failure detail.";
        }

        var parts = new List<string> { failure.Message };
        if (!string.IsNullOrWhiteSpace(failure.AccountAlias))
        {
            parts.Add($"account={failure.AccountAlias}");
        }

        if (!string.IsNullOrWhiteSpace(failure.TestRecordId))
        {
            parts.Add($"testRecordId={failure.TestRecordId}");
        }

        if (!string.IsNullOrWhiteSpace(failure.FixturePath))
        {
            parts.Add($"fixturePath={failure.FixturePath}");
        }

        if (failure.TimeoutName is not null)
        {
            parts.Add($"timeout={failure.TimeoutName}");
        }

        return string.Join("; ", parts);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "orient-dotnet.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate orient-dotnet.sln from test output directory.");
    }
}
