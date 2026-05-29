namespace LordUnion.IntegrationTests.Config;

public static class LordUnionTestConfigValidator
{
    public const int RequiredAccountCount = 3;

    public static IReadOnlyList<string> Validate(LordUnionTestConfig config)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(config.Server.Host))
        {
            errors.Add("Server.Host is required.");
        }

        if (config.Server.Port is <= 0 or > 65535)
        {
            errors.Add($"Server.Port must be between 1 and 65535 (got {config.Server.Port}).");
        }

        if (config.Accounts.Count != RequiredAccountCount)
        {
            errors.Add($"Exactly {RequiredAccountCount} accounts are required for V1 (got {config.Accounts.Count}).");
        }

        for (var i = 0; i < config.Accounts.Count; i++)
        {
            var account = config.Accounts[i];
            var prefix = $"Accounts[{i}]";

            if (string.IsNullOrWhiteSpace(account.Alias))
            {
                errors.Add($"{prefix}.Alias is required.");
            }

            if (string.IsNullOrWhiteSpace(account.Username))
            {
                errors.Add($"{prefix}.Username is required.");
            }

            if (string.IsNullOrWhiteSpace(account.Password))
            {
                errors.Add($"{prefix}.Password is required.");
            }
        }

        var duplicateAliases = config.Accounts
            .Where(a => !string.IsNullOrWhiteSpace(a.Alias))
            .GroupBy(a => a.Alias, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateAliases.Count > 0)
        {
            errors.Add($"Duplicate account aliases: {string.Join(", ", duplicateAliases)}.");
        }

        if (config.Match.GameId == 0)
        {
            errors.Add("Match.GameId is required.");
        }

        if (config.Match.ProductId == 0)
        {
            errors.Add("Match.ProductId is required.");
        }

        if (config.Match.TourneyId == 0)
        {
            errors.Add("Match.TourneyId is required.");
        }

        if (string.IsNullOrWhiteSpace(config.Output.Directory))
        {
            errors.Add("Output.Directory is required.");
        }

        ValidateTimeout(errors, nameof(config.Timeouts.ConnectTimeout), config.Timeouts.ConnectTimeout);
        ValidateTimeout(errors, nameof(config.Timeouts.LoginTimeout), config.Timeouts.LoginTimeout);
        ValidateTimeout(errors, nameof(config.Timeouts.SignupTimeout), config.Timeouts.SignupTimeout);
        ValidateTimeout(errors, nameof(config.Timeouts.MatchStartTimeout), config.Timeouts.MatchStartTimeout);
        ValidateTimeout(errors, nameof(config.Timeouts.EnterMatchTimeout), config.Timeouts.EnterMatchTimeout);
        ValidateTimeout(errors, nameof(config.Timeouts.EnterRoundTimeout), config.Timeouts.EnterRoundTimeout);
        ValidateTimeout(errors, nameof(config.Timeouts.GameActionTimeout), config.Timeouts.GameActionTimeout);
        ValidateTimeout(errors, nameof(config.Timeouts.GameOverTimeout), config.Timeouts.GameOverTimeout);

        return errors;
    }

    private static void ValidateTimeout(List<string> errors, string name, TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            errors.Add($"{name} must be greater than zero.");
        }
    }
}