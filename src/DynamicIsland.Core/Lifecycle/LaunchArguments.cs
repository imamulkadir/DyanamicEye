namespace DynamicIsland.Core.Lifecycle;

/// <summary>
/// Command-line contract of the app (launch arguments are untrusted input, guide §4.6). Only allow-listed switches
/// are honoured; everything else is counted and ignored, never interpreted.
/// </summary>
/// <param name="Startup">Started by the sign-in Run entry (ADR 0015): start quietly, never show onboarding.</param>
public sealed record LaunchArguments(bool SafeMode, int IgnoredCount, bool Startup = false)
{
    public const string SafeModeSwitch = "--safe-mode";

    /// <summary>Marks a sign-in start (launcher and app).</summary>
    public const string StartupSwitch = "--startup";

    public static LaunchArguments Default { get; } = new(SafeMode: false, IgnoredCount: 0);

    public static LaunchArguments Parse(IReadOnlyCollection<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        return new LaunchArguments(
            args.Any(a => Is(a, SafeModeSwitch)),
            args.Count(a => !Is(a, SafeModeSwitch) && !Is(a, StartupSwitch)),
            args.Any(a => Is(a, StartupSwitch)));
    }

    private static bool Is(string? argument, string expected) => string.Equals(argument, expected, StringComparison.OrdinalIgnoreCase);
}
