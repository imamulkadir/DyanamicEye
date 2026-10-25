namespace DynamicIsland.Updater;

internal static class Program
{
    private const int NotImplementedExitCode = 1;

    private static int Main()
    {
        // Update installation is implemented in P06. Fail loudly rather than pretend to work.
        Console.Error.WriteLine("DynamicIsland.Updater is not implemented yet.");
        return NotImplementedExitCode;
    }
}
