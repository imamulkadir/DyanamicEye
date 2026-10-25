using System.Runtime.InteropServices;

namespace DynamicIsland.Platform.Windows;

internal static partial class NativeMethods
{
    /// <summary><c>ASFW_ANY</c>: any process may set the foreground window.</summary>
    public const int AllowAnyProcess = -1;

    /// <summary>Lets the already-running instance bring its window forward when the user launches the app again.</summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AllowSetForegroundWindow(int processId);
}
