using DynamicIsland.Core.Settings;

namespace DynamicIsland.UnitTests.Settings;

public sealed class SettingsServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Update_validates_saves_publishes_and_becomes_current()
    {
        var store = new RecordingStore();
        using var service = new SettingsService(store);
        AppSettings? published = null;
        service.Changed += (_, s) => published = s;

        var result = await service.UpdateAsync(s => s with { Appearance = s.Appearance with { Opacity = 7 } }, Ct);

        Assert.Equal(SettingsSaveResult.Saved, result);
        Assert.Equal(SettingsValidator.MaxOpacity, service.Current.Appearance.Opacity);
        Assert.Same(service.Current, published);
        Assert.Same(service.Current, Assert.Single(store.Saved));
    }

    [Fact]
    public async Task Read_only_store_applies_the_change_for_this_session_only()
    {
        var store = new RecordingStore { ReadOnly = true };
        using var service = new SettingsService(store);

        var result = await service.UpdateAsync(s => s with { General = s.General with { PreferredMonitorId = "x" } }, Ct);

        Assert.Equal(SettingsSaveResult.SkippedReadOnly, result);
        Assert.True(service.IsReadOnly);
        Assert.Equal("x", service.Current.General.PreferredMonitorId);
    }

    [Fact]
    public async Task Failed_save_keeps_the_previous_settings_and_publishes_nothing()
    {
        var store = new RecordingStore { Failure = new IOException("disk full") };
        using var service = new SettingsService(store);
        var changed = false;
        service.Changed += (_, _) => changed = true;
        var before = service.Current;

        await Assert.ThrowsAsync<IOException>(() => service.UpdateAsync(s => s with { Appearance = s.Appearance with { Opacity = 0.5 } }, Ct));

        Assert.Same(before, service.Current);
        Assert.False(changed);
    }

    [Fact]
    public async Task Concurrent_updates_are_applied_one_after_another()
    {
        var store = new RecordingStore();
        using var service = new SettingsService(store);

        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() =>
            service.UpdateAsync(s => s with { Behavior = s.Behavior with { HoverPreviewDelayMs = s.Behavior.HoverPreviewDelayMs + 10 } }, Ct), Ct)));

        Assert.Equal(300 + (20 * 10), service.Current.Behavior.HoverPreviewDelayMs);
    }

    [Fact]
    public void Initialize_normalizes_loaded_settings_without_saving()
    {
        var store = new RecordingStore();
        using var service = new SettingsService(store);

        service.Initialize(new AppSettings { Appearance = new AppearanceSettings { Scale = 99 } });

        Assert.Equal(SettingsValidator.MaxScale, service.Current.Appearance.Scale);
        Assert.Empty(store.Saved);
    }

    private sealed class RecordingStore : ISettingsStore
    {
        public List<AppSettings> Saved { get; } = [];

        public bool ReadOnly { get; init; }

        public Exception? Failure { get; init; }

        public bool IsReadOnly => ReadOnly;

        public Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new SettingsLoadResult(new AppSettings(), SettingsLoadOutcome.CreatedDefaults, []));

        public Task<SettingsSaveResult> SaveAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            if (ReadOnly)
            {
                return Task.FromResult(SettingsSaveResult.SkippedReadOnly);
            }

            lock (Saved)
            {
                Saved.Add(settings);
            }

            return Task.FromResult(SettingsSaveResult.Saved);
        }
    }
}
