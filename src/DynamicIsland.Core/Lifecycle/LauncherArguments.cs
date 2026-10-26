using System.Globalization;

namespace DynamicIsland.Core.Lifecycle;

/// <summary>
/// Launcher command line (untrusted input, guide §4.6): only <c>--safe-mode</c>, <c>--startup</c> and
/// <c>--after-pid &lt;positive int&gt;</c> are honoured; everything else is counted and ignored.
/// </summary>
public sealed record LauncherArguments(bool SafeMode, int? AfterProcessId, int IgnoredCount, bool Startup = false)
{
    public static LauncherArguments Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var safeMode = false;
        var startup = false;
        int? afterPid = null;
        var ignored = 0;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, LaunchArguments.SafeModeSwitch, StringComparison.OrdinalIgnoreCase))
            {
                safeMode = true;
            }
            else if (string.Equals(arg, LaunchArguments.StartupSwitch, StringComparison.OrdinalIgnoreCase))
            {
                startup = true;
            }
            else if (string.Equals(arg, LauncherContract.AfterPidSwitch, StringComparison.OrdinalIgnoreCase)
                     && i + 1 < args.Count
                     && int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var pid)
                     && pid > 0)
            {
                afterPid = pid;
                i++;
            }
            else
            {
                ignored++;
            }
        }

        return new LauncherArguments(safeMode, afterPid, ignored, startup);
    }
}
