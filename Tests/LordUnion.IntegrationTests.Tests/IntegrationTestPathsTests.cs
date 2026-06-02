using LordUnion.IntegrationTests.Config;

namespace LordUnion.IntegrationTests.Tests;

public class IntegrationTestPathsTests
{
    [Fact]
    public void ResolveOutputDirectory_RelativePath_IsUnderIntegrationTestProject()
    {
        var resolved = IntegrationTestPaths.ResolveOutputDirectory("lordunion-test-output");

        Assert.EndsWith(
            Path.Combine("LordUnion.IntegrationTests", "lordunion-test-output"),
            resolved,
            StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(IntegrationTestPaths.GetProjectDirectory()));
    }

    [Fact]
    public void ResolveOutputDirectory_AbsolutePath_IsNormalized()
    {
        var absolute = Path.Combine(Path.GetTempPath(), "lordunion-abs-test");
        var resolved = IntegrationTestPaths.ResolveOutputDirectory(absolute);

        Assert.Equal(Path.GetFullPath(absolute), resolved);
    }
}
