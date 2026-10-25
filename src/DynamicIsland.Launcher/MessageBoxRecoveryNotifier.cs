using System.Runtime.InteropServices;
using DynamicIsland.Core.Diagnostics;
using DynamicIsland.Core.Lifecycle;

namespace DynamicIsland.Launcher;

/// <summary>Native message box with manual recovery steps. Suppressed by the Debug-only test hook.</summary>
internal sealed partial class MessageBoxRecoveryNotifier(string logsFolder) : IRecoveryNotifier
{
    private const string Caption = "Dynamic Island";
    private const uint OkWithWarningIcon = 0x00000000 | 0x00000030;

    public void NotifyStopped() => Show(
        "Dynamic Island stopped because it kept closing unexpectedly, including in Safe Mode.\n\n" +
        $"Logs: {logsFolder}\n\n" +
        "To try again, start Dynamic Island from the Start menu. If it keeps happening, report the problem on GitHub " +
        "and attach the log after reviewing it.");

    public static void NotifyAppMissing() => Show(
        "Dynamic Island could not be started. The app files may be missing or blocked.\n\n" +
        "Reinstall Dynamic Island, or download the portable version again from the official GitHub Releases page.");

    private static void Show(string message)
    {
        if (TestHooks.Get(TestHooks.NoDialogsVariable) == "1")
        {
            return;
        }

        _ = MessageBoxW(IntPtr.Zero, message, Caption, OkWithWarningIcon);
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int MessageBoxW(IntPtr owner, string text, string caption, uint type);
}
