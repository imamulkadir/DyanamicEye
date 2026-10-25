using DynamicIsland.Core.Settings;

namespace DynamicIsland.App;

/// <summary>
/// Settings loaded at startup, shared with the shell. Replaced by a change-notifying settings service when the
/// settings window arrives (P03).
/// </summary>
public sealed class AppSettingsSnapshot
{
    private AppSettings _current = new();

    public AppSettings Current
    {
        get => Volatile.Read(ref _current);
        set => Volatile.Write(ref _current, value ?? throw new ArgumentNullException(nameof(value)));
    }
}
