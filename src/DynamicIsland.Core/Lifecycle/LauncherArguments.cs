using System.Globalization;

namespace DynamicIsland.Core.Lifecycle;

/// <summary>
/// Launcher command line (untrusted input, guide §4.6): only <c>--safe-mode</c> and <c>--after-pid &lt;positive int&gt;</c>
/// are honoured; everything else is counted and ignored.
/// </summary>
public sealed record LauncherArguments(bool SafeMode, int? AfterProcessId, int IgnoredCount)
{
    public static LauncherArguments Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var safeMode = false;
        int? afterPid = null;
        var ignored = 0;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, LaunchArguments.SafeModeSwitch, StringComparison.OrdinalIgnoreCase))
            {
                safeMode = true;
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

        return new LauncherArguments(safeMode, afterPid, ignored);
    }
}
