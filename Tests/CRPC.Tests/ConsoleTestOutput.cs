namespace CRPC.Tests;

/// <summary>
/// Serializes <see cref="Console.SetOut"/> redirection for parallel xUnit runs.
/// </summary>
internal static class ConsoleTestOutput
{
    private static readonly object Gate = new();

    public static string Capture(Action action)
    {
        lock (Gate)
        {
            var originalOut = Console.Out;
            using var writer = new StringWriter();
            try
            {
                Console.SetOut(writer);
                action();
                return writer.ToString();
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }
    }
}
