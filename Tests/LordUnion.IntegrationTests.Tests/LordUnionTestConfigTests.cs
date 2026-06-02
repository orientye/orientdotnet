using LordUnion.IntegrationTests.Config;

namespace LordUnion.IntegrationTests.Tests;

public class LordUnionTestConfigTests
{
    [Fact]
    public void LoadExampleConfig_HasExpectedDefaults()
    {
        var configPath = GetExampleConfigPath();
        var config = LordUnionTestConfigLoader.Load(configPath);

        Assert.Equal(LordUnionTestConfigDefaults.DefaultHost, config.Server.Host);
        Assert.Equal(LordUnionTestConfigDefaults.DefaultPort, config.Server.Port);
        Assert.Equal(LordUnionTestConfigDefaults.DefaultAppId, config.Protocol.AppId);
        Assert.Equal(LordUnionTestConfigDefaults.DefaultGameId, config.Match.GameId);
        Assert.Equal(LordUnionTestConfigDefaults.DefaultProductId, config.Match.ProductId);
        Assert.Equal(LordUnionTestConfigDefaults.DefaultTourneyId, config.Match.TourneyId);
        Assert.Equal(3, config.Accounts.Count);
        Assert.Equal("player1", config.Accounts[0].Alias);
    }

    [Fact]
    public void ValidateExampleConfig_Passes()
    {
        var configPath = GetExampleConfigPath();
        var config = LordUnionTestConfigLoader.Load(configPath);

        var errors = LordUnionTestConfigValidator.Validate(config);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_MissingAccountAlias_Fails()
    {
        var config = LordUnionTestConfigLoader.Load(GetExampleConfigPath());
        config.Accounts[0].Alias = string.Empty;

        var errors = LordUnionTestConfigValidator.Validate(config);

        Assert.Contains(errors, e => e.Contains("Accounts[0].Alias"));
    }

    [Fact]
    public void Validate_WrongAccountCount_Fails()
    {
        var config = LordUnionTestConfigLoader.Load(GetExampleConfigPath());
        config.Accounts.RemoveAt(2);

        var errors = LordUnionTestConfigValidator.Validate(config);

        Assert.Contains(errors, e => e.Contains("Exactly 3 accounts"));
    }

    private static string GetExampleConfigPath()
    {
        var repoRoot = FindRepoRoot();
        return Path.Combine(repoRoot, "Tests", "LordUnion.IntegrationTests", "appsettings.example.json");
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
