namespace DynamicIsland.Core.Settings;

/// <summary>Current settings for the running app, with validated, persisted, serialized updates.</summary>
public interface ISettingsService
{
    /// <summary>Raised after a successful update with the new settings (on the updating thread).</summary>
    event EventHandler<AppSettings>? Changed;

    AppSettings Current { get; }

    /// <summary>True when changes cannot be saved (file from a newer version or failed migration, ADR 0003).</summary>
    bool IsReadOnly { get; }

    /// <summary>Applies <paramref name="change"/> to the current settings, validates, saves and raises <see cref="Changed"/>.</summary>
    /// <returns><see cref="SettingsSaveResult.SkippedReadOnly"/> when the change is applied for this session only.</returns>
    Task<SettingsSaveResult> UpdateAsync(Func<AppSettings, AppSettings> change, CancellationToken cancellationToken);
}

/// <summary>
/// Owns the in-memory settings. Updates are serialized; every result is normalized by <see cref="SettingsValidator"/>
/// before it is saved or published. When the store is read-only the change still applies for this session (the user
/// sees it working) but is not persisted, and the caller is told so.
/// </summary>
public sealed class SettingsService(ISettingsStore store) : ISettingsService, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AppSettings _current = new();

    public event EventHandler<AppSettings>? Changed;

    public AppSettings Current => Volatile.Read(ref _current);

    public bool IsReadOnly => store.IsReadOnly;

    /// <summary>Sets the settings loaded at startup (no save, no event).</summary>
    public void Initialize(AppSettings loaded) =>
        Volatile.Write(ref _current, SettingsValidator.Normalize(loaded).Settings);

    public async Task<SettingsSaveResult> UpdateAsync(Func<AppSettings, AppSettings> change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        AppSettings updated;
        SettingsSaveResult result;
        try
        {
            updated = SettingsValidator.Normalize(change(Current)).Settings;
            result = await store.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _current, updated);
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, updated);
        return result;
    }

    public void Dispose() => _gate.Dispose();
}
