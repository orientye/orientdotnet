namespace LordUnion.IntegrationTests.Config;

public static class IntegrationTestPaths
{
    private const string ProjectFileName = "LordUnion.IntegrationTests.csproj";

    public static string ResolveOutputDirectory(string configuredDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configuredDirectory);

        if (Path.IsPathRooted(configuredDirectory))
        {
            return Path.GetFullPath(configuredDirectory);
        }

        return Path.GetFullPath(Path.Combine(GetProjectDirectory(), configuredDirectory));
    }

    public static string GetProjectDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var inCurrent = Path.Combine(current.FullName, ProjectFileName);
            if (File.Exists(inCurrent))
            {
                return current.FullName;
            }

            var underTests = Path.Combine(current.FullName, "Tests", "LordUnion.IntegrationTests", ProjectFileName);
            if (File.Exists(underTests))
            {
                return Path.GetDirectoryName(underTests)!;
            }

            current = current.Parent;
        }

        return AppContext.BaseDirectory;
    }
}
