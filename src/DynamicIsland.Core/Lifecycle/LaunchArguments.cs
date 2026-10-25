namespace DynamicIsland.Core.Lifecycle;

/// <summary>
/// Command-line contract of the app (launch arguments are untrusted input, guide §4.6). Only allow-listed switches
/// are honoured; everything else is counted and ignored, never interpreted.
/// </summary>
public sealed record LaunchArguments(bool SafeMode, int IgnoredCount)
{
    public const string SafeModeSwitch = "--safe-mode";

    public static LaunchArguments Default { get; } = new(SafeMode: false, IgnoredCount: 0);

    public static LaunchArguments Parse(IReadOnlyCollection<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var safeMode = args.Any(IsSafeMode);
        return new LaunchArguments(safeMode, args.Count(a => !IsSafeMode(a)));
    }

    private static bool IsSafeMode(string? argument) => string.Equals(argument, SafeModeSwitch, StringComparison.OrdinalIgnoreCase);
}
