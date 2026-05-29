using System.Text.Json;
using System.Text.Json.Serialization;

namespace LordUnion.IntegrationTests.Config;

public static class LordUnionTestConfigLoader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static LordUnionTestConfig Load(string configPath)
    {
        if (string.IsNullOrWhiteSpace(configPath))
        {
            throw new ArgumentException("Config path is required.", nameof(configPath));
        }

        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException($"Config file not found: {configPath}", configPath);
        }

        var json = File.ReadAllText(configPath);
        var config = JsonSerializer.Deserialize<LordUnionTestConfig>(json, SerializerOptions)
                     ?? throw new InvalidOperationException($"Failed to deserialize config: {configPath}");

        ApplyDefaults(config);
        return config;
    }

    private static void ApplyDefaults(LordUnionTestConfig config)
    {
        config.Server ??= new ServerConfig();
        config.Protocol ??= new ProtocolConfig();
        config.Match ??= new MatchConfig();
        config.Timeouts ??= new TimeoutConfig();
        config.Bot ??= new BotConfig();
        config.Bot.PacingOptions ??= new BotPacingOptions();
        config.Output ??= new OutputConfig();

        if (config.Accounts.Count == 0)
        {
            config.Accounts = LordUnionTestConfigDefaults.CreateDefaultAccounts();
        }
    }
}