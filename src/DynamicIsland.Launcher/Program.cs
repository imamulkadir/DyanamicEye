namespace DynamicIsland.Launcher;

internal static class Program
{
    private const int NotImplementedExitCode = 1;

    private static int Main()
    {
        // Crash supervision is implemented in P01. Fail loudly rather than pretend to work.
        Console.Error.WriteLine("DynamicIsland.Launcher is not implemented yet.");
        return NotImplementedExitCode;
    }
}
