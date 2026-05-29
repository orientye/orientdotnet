using LordUnion.IntegrationTests.Config;
using LordUnion.IntegrationTests.Reporting;
using LordUnion.IntegrationTests.Scenarios;

namespace LordUnion.IntegrationTests;

public static class Program
{
    private const string DefaultLocalConfigFileName = "appsettings.local.json";
    private const string ExampleConfigFileName = "appsettings.example.json";

    public static int Main(string[] args)
    {
        CliOptions options;
        try
        {
            options = CliOptions.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            PrintHelp();
            return 1;
        }

        if (options.ShowHelp)
        {
            PrintHelp();
            return 0;
        }

        var configPath = ResolveConfigPath(options);
        if (configPath is null)
        {
            return 1;
        }

        LordUnionTestConfig config;
        try
        {
            config = LordUnionTestConfigLoader.Load(configPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load config from '{configPath}': {ex.Message}");
            return 1;
        }

        var validationErrors = LordUnionTestConfigValidator.Validate(config);
        if (validationErrors.Count > 0)
        {
            Console.Error.WriteLine("Config validation failed:");
            foreach (var error in validationErrors)
            {
                Console.Error.WriteLine($"  - {error}");
            }

            return 1;
        }

        PrintConfigSummary(config, configPath);

        if (!options.Live)
        {
            Console.WriteLine();
            Console.WriteLine("Dry run complete. Live server execution is disabled.");
            Console.WriteLine(
                $"Re-run with --live --config \"{DefaultLocalConfigFileName}\" to connect to the real server.");
            return 0;
        }

        Console.WriteLine();
        Console.WriteLine("Live mode enabled. Running scenario...");

        var startedAt = DateTimeOffset.UtcNow;
        var report = ThreePlayersOneGameScenario.RunHosted(
            config,
            new ScenarioRunOptions { UseLiveTransport = true });
        var endedAt = DateTimeOffset.UtcNow;

        var metadata = new ReportMetadata
        {
            StartedAt = startedAt,
            EndedAt = endedAt,
        };

        var outputDirectory = IntegrationTestPaths.ResolveOutputDirectory(config.Output.Directory);
        var writer = new ReportWriter();
        writer.WriteConsoleSummary(report, metadata);
        var jsonPath = writer.WriteJsonReport(report, metadata, outputDirectory);
        Console.WriteLine();
        Console.WriteLine($"JSON report: {jsonPath}");

        return report.Success ? 0 : 1;
    }

    private static string? ResolveConfigPath(CliOptions options)
    {
        if (options.Live)
        {
            if (string.IsNullOrWhiteSpace(options.ConfigPath))
            {
                Console.Error.WriteLine(
                    $"Live mode requires --config <path> pointing to a local config file (for example {DefaultLocalConfigFileName}).");
                return null;
            }

            if (!File.Exists(options.ConfigPath))
            {
                Console.Error.WriteLine($"Live mode config file not found: {options.ConfigPath}");
                Console.Error.WriteLine(
                    $"Copy {ExampleConfigFileName} to {DefaultLocalConfigFileName} and fill in credentials.");
                return null;
            }

            if (IsExampleConfigPath(options.ConfigPath))
            {
                Console.Error.WriteLine(
                    "Live mode cannot use the example config file. Use a local config such as appsettings.local.json.");
                return null;
            }

            return options.ConfigPath;
        }

        if (!string.IsNullOrWhiteSpace(options.ConfigPath))
        {
            if (!File.Exists(options.ConfigPath))
            {
                Console.Error.WriteLine($"Config file not found: {options.ConfigPath}");
                return null;
            }

            return options.ConfigPath;
        }

        var localConfig = FindConfigNearApp(DefaultLocalConfigFileName);
        if (localConfig is not null)
        {
            return localConfig;
        }

        var exampleConfig = FindConfigNearApp(ExampleConfigFileName);
        if (exampleConfig is not null)
        {
            Console.WriteLine($"No --config specified; validating {ExampleConfigFileName}.");
            return exampleConfig;
        }

        Console.Error.WriteLine(
            $"No config file found. Pass --config <path> or place {ExampleConfigFileName} next to the runner.");
        return null;
    }

    private static bool IsExampleConfigPath(string configPath)
    {
        var fileName = Path.GetFileName(configPath);
        return fileName.Equals(ExampleConfigFileName, StringComparison.OrdinalIgnoreCase)
               || fileName.EndsWith(".example.json", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindConfigNearApp(string fileName)
    {
        var baseDirectory = AppContext.BaseDirectory;
        var candidate = Path.Combine(baseDirectory, fileName);
        return File.Exists(candidate) ? candidate : null;
    }

    private static void PrintConfigSummary(LordUnionTestConfig config, string configPath)
    {
        Console.WriteLine($"Config: {configPath}");
        Console.WriteLine($"Server: {config.Server.Host}:{config.Server.Port}");
        Console.WriteLine($"Protocol: appId={config.Protocol.AppId}, loginType={config.Protocol.LoginType}");
        Console.WriteLine(
            $"Match: gameId={config.Match.GameId}, productId={config.Match.ProductId}, tourneyId={config.Match.TourneyId}");
        Console.WriteLine($"Accounts: {string.Join(", ", config.Accounts.Select(a => a.Alias))}");
        Console.WriteLine($"Output: {IntegrationTestPaths.ResolveOutputDirectory(config.Output.Directory)}");
    }

    private static void PrintHelp()
    {
        Console.WriteLine("LordUnion integration test runner");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  LordUnion.IntegrationTests [--config <path>]");
        Console.WriteLine("  LordUnion.IntegrationTests --live --config <path>");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine(
            "  --config <path>   Config JSON path (dry run defaults to appsettings.example.json when present)");
        Console.WriteLine(
            "  --live            Enable live server execution (requires a local config file, not *.example.json)");
        Console.WriteLine("  -h, --help        Show this help");
    }

    private sealed class CliOptions
    {
        public bool Live { get; init; }

        public string? ConfigPath { get; init; }

        public bool ShowHelp { get; init; }

        public static CliOptions Parse(string[] args)
        {
            var live = false;
            string? configPath = null;
            var showHelp = false;

            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                switch (arg)
                {
                    case "--live":
                        live = true;
                        break;
                    case "--config":
                        if (i + 1 >= args.Length)
                        {
                            throw new ArgumentException("--config requires a path argument.");
                        }

                        configPath = args[++i];
                        break;
                    case "-h":
                    case "--help":
                        showHelp = true;
                        break;
                    default:
                        throw new ArgumentException($"Unknown argument: {arg}");
                }
            }

            return new CliOptions
            {
                Live = live,
                ConfigPath = configPath,
                ShowHelp = showHelp,
            };
        }
    }
}