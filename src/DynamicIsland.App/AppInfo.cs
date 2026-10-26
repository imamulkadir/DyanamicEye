using System.Reflection;
using DynamicIsland.Core.Settings;

namespace DynamicIsland.App;

/// <summary>Facts about the running app for logs, the About page and diagnostic reports (no personal data).</summary>
public sealed class AppInfo
{
    private static readonly Assembly Assembly = typeof(AppInfo).Assembly;

    /// <summary>Full informational version, including the source revision when the build adds one.</summary>
    public string Version { get; } = Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    /// <summary><see cref="Version"/> without the build metadata (<c>+revision</c>), for people.</summary>
    public string DisplayVersion => Version.Split('+')[0];

    public string Copyright { get; } = Assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? string.Empty;

    /// <summary>How the settings were loaded at startup; set once loading finished.</summary>
    public SettingsLoadOutcome? SettingsOutcome { get; set; }
}
