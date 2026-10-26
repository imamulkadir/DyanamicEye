using Microsoft.Win32;

namespace DynamicIsland.Platform.Windows.Display;

/// <summary>
/// The Windows "app mode" (Settings › Personalization › Colors), read from the current user's registry. Windows
/// broadcasts <c>WM_SETTINGCHANGE</c> with <see cref="ChangeMessage"/> when it changes, so no polling is needed.
/// </summary>
public static class WindowsAppTheme
{
    /// <summary>The <c>lParam</c> string of the <c>WM_SETTINGCHANGE</c> sent when the app mode or accent changes.</summary>
    public const string ChangeMessage = "ImmersiveColorSet";

    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary><c>true</c> when apps use the light mode; a missing or unreadable value means dark (the classic island).</summary>
    public static bool AppsUseLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}
