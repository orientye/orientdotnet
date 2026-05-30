using CRpc.Transport;

namespace LordUnion.IntegrationTests.Config;

public sealed class LordUnionTestConfig
{
    public ServerConfig Server { get; set; } = new();

    public ProtocolConfig Protocol { get; set; } = new();

    public MatchConfig Match { get; set; } = new();

    public List<AccountConfig> Accounts { get; set; } = LordUnionTestConfigDefaults.CreateDefaultAccounts();

    public TimeoutConfig Timeouts { get; set; } = new();

    public BotConfig Bot { get; set; } = new();

    public OutputConfig Output { get; set; } = new();

    public LiveConfig Live { get; set; } = new();
}

public sealed class LiveConfig
{
    public int IoThreadCount { get; set; } = TcpChannelHostOptions.DefaultIoThreadCount;
}

public sealed class BotConfig
{
    /// <summary>
    /// Pacing mode: immediate, human-random, server-countdown (reserved), replay (reserved).
    /// </summary>
    public string Pacing { get; set; } = "human-random";

    public BotPacingOptions PacingOptions { get; set; } = new();
}

public sealed class BotPacingOptions
{
    public int BidMinMs { get; set; } = 1000;

    public int BidMaxMs { get; set; } = 3000;

    public int PlayMinMs { get; set; } = 2000;

    public int PlayMaxMs { get; set; } = 6000;

    public int AliasJitterMaxMs { get; set; } = 500;
}

public sealed class ServerConfig
{
    public string Host { get; set; } = LordUnionTestConfigDefaults.DefaultHost;

    public int Port { get; set; } = LordUnionTestConfigDefaults.DefaultPort;
}

public sealed class ProtocolConfig
{
    public uint AppId { get; set; } = LordUnionTestConfigDefaults.DefaultAppId;

    public uint AnonymousSerialId { get; set; } = LordUnionTestConfigDefaults.DefaultAnonymousSerialId;

    public uint LoginType { get; set; } = LordUnionTestConfigDefaults.DefaultLoginType;
}

public sealed class MatchConfig
{
    public uint GameId { get; set; } = LordUnionTestConfigDefaults.DefaultGameId;

    public uint ProductId { get; set; } = LordUnionTestConfigDefaults.DefaultProductId;

    public uint TourneyId { get; set; } = LordUnionTestConfigDefaults.DefaultTourneyId;
}

public sealed class AccountConfig
{
    public string Alias { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;
}

public sealed class TimeoutConfig
{
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);

    public TimeSpan LoginTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan SignupTimeout { get; set; } = TimeSpan.FromSeconds(60);

    public TimeSpan MatchStartTimeout { get; set; } = TimeSpan.FromSeconds(120);

    public TimeSpan EnterMatchTimeout { get; set; } = TimeSpan.FromSeconds(60);

    public TimeSpan EnterRoundTimeout { get; set; } = TimeSpan.FromSeconds(60);

    public TimeSpan GameActionTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan GameOverTimeout { get; set; } = TimeSpan.FromSeconds(180);
}

public sealed class OutputConfig
{
    public string Directory { get; set; } = LordUnionTestConfigDefaults.DefaultOutputDirectory;
}

public static class LordUnionTestConfigDefaults
{
    public const string DefaultHost = "115.182.5.66";
    public const int DefaultPort = 30301;
    public const uint DefaultAppId = 2;
    public const uint DefaultAnonymousSerialId = 1;
    public const uint DefaultLoginType = 2;
    public const uint DefaultGameId = 1001;
    public const uint DefaultProductId = 2008280;
    public const uint DefaultTourneyId = 159740;
    public const string DefaultOutputDirectory = "lordunion-test-output";

    public static List<AccountConfig> CreateDefaultAccounts() =>
    [
        new AccountConfig { Alias = "player1", Username = "TJJ006628", Password = "3YXRQW" },
        new AccountConfig { Alias = "player2", Username = "TJJ006629", Password = "3YRQ83" },
        new AccountConfig { Alias = "player3", Username = "TJJ006630", Password = "Q5EDHU" },
    ];
}