using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace DynamicIsland.App;

/// <summary>Opens folders in File Explorer and web links in the default browser, without elevation.</summary>
internal static class ShellLauncher
{
    /// <summary>Creates the folder if needed and opens it.</summary>
    /// <returns>The exception type name on failure (for logging), otherwise <c>null</c>.</returns>
    public static string? TryOpenFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var startInfo = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
            startInfo.ArgumentList.Add(folder);
            Process.Start(startInfo)?.Dispose();
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            return ex.GetType().Name;
        }
    }

    /// <summary>Opens an https link (only https is accepted) in the default browser.</summary>
    /// <returns>The exception type name on failure (for logging), otherwise <c>null</c>.</returns>
    public static string? TryOpenLink(Uri link)
    {
        ArgumentNullException.ThrowIfNull(link);
        if (link.Scheme != Uri.UriSchemeHttps)
        {
            return nameof(ArgumentException);
        }

        try
        {
            Process.Start(new ProcessStartInfo(link.AbsoluteUri) { UseShellExecute = true })?.Dispose();
            return null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return ex.GetType().Name;
        }
    }
}
