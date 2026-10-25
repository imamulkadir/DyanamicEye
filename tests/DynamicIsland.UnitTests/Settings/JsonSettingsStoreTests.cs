using System.Text;
using System.Text.Json.Nodes;
using DynamicIsland.Core.Settings;
using DynamicIsland.Infrastructure.Settings;
using DynamicIsland.UnitTests.TestSupport;

namespace DynamicIsland.UnitTests.Settings;

public sealed class JsonSettingsStoreTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 13, 0, 0, TimeSpan.Zero);

    private readonly TempDirectory _directory = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string SettingsPath => _directory.Combine(JsonSettingsStore.FileName);

    public void Dispose() => _directory.Dispose();

    [Fact]
    public async Task Load_without_file_returns_defaults_and_writes_nothing()
    {
        using var store = CreateStore();

        var result = await store.LoadAsync(Ct);

        Assert.Equal(SettingsLoadOutcome.CreatedDefaults, result.Outcome);
        AssertDefaults(result.Settings);
        Assert.Empty(_directory.FileNames());
        Assert.False(store.IsReadOnly);
    }

    [Fact]
    public async Task Saved_settings_round_trip()
    {
        var settings = new AppSettings
        {
            General = new GeneralSettings { StartWithWindows = ConsentState.Granted, PreferredMonitorId = @"\\.\DISPLAY2" },
            Appearance = new AppearanceSettings
            {
                Theme = ThemeMode.Dark,
                Scale = 1.25,
                Opacity = 0.8,
                Placement = IslandPlacement.TopRight,
                Motion = MotionPreference.Reduced,
            },
            Behavior = new BehaviorSettings { HoverPreviewEnabled = false, HoverPreviewDelayMs = 750, AutoHideInFullscreen = false },
            Widgets = WidgetIds.All.Reverse().Select(id => new WidgetSetting(id, id != WidgetIds.Media)).ToArray(),
            Updates = new UpdateSettings { AutomaticChecks = ConsentState.Declined },
        };

        using (var writer = CreateStore())
        {
            Assert.Equal(SettingsSaveResult.Saved, await writer.SaveAsync(settings, Ct));
        }

        using var reader = CreateStore();
        var result = await reader.LoadAsync(Ct);

        Assert.Equal(SettingsLoadOutcome.Loaded, result.Outcome);
        Assert.Empty(result.Corrections);
        AssertEquivalent(settings, result.Settings);
    }

    [Fact]
    public async Task Saved_file_is_versioned_json_with_readable_enums()
    {
        using var store = CreateStore();

        await store.SaveAsync(new AppSettings { Appearance = new AppearanceSettings { Theme = ThemeMode.Dark } }, Ct);

        var json = await File.ReadAllTextAsync(SettingsPath, Ct);
        Assert.Contains("\"schemaVersion\": 1", json, StringComparison.Ordinal);
        Assert.Contains("\"theme\": \"Dark\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Save_is_atomic_and_keeps_previous_file_as_backup()
    {
        using var store = CreateStore();

        await store.SaveAsync(WithOpacity(0.5), Ct);
        var first = await File.ReadAllBytesAsync(SettingsPath, Ct);
        await store.SaveAsync(WithOpacity(0.6), Ct);

        Assert.Equal([JsonSettingsStore.FileName, JsonSettingsStore.BackupFileName], _directory.FileNames());
        Assert.Equal(first, await File.ReadAllBytesAsync(_directory.Combine(JsonSettingsStore.BackupFileName), Ct));
    }

    [Fact]
    public async Task Save_normalizes_invalid_values_before_writing()
    {
        using var store = CreateStore();

        await store.SaveAsync(WithOpacity(5), Ct);

        var result = await store.LoadAsync(Ct);
        Assert.Equal(SettingsValidator.MaxOpacity, result.Settings.Appearance.Opacity);
        Assert.Empty(result.Corrections);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("\"text\"")]
    [InlineData("{\"schemaVersion\":\"one\"}")]
    [InlineData("{\"schemaVersion\":1.5}")]
    [InlineData("{\"schemaVersion\":-3}")]
    [InlineData("{\"schemaVersion\":1,\"schemaVersion\":1}")]
    [InlineData("{\"schemaVersion\":1,\"appearance\":{\"opacity\":\"high\"}}")]
    [InlineData("{\"schemaVersion\":1,\"appearance\":{\"theme\":\"Neon\"}}")]
    [InlineData("{\"appearance\":{}}")]
    public async Task Corrupt_file_is_quarantined_and_defaults_used(string content)
    {
        await File.WriteAllTextAsync(SettingsPath, content, Ct);
        using var store = CreateStore();

        var result = await store.LoadAsync(Ct);

        Assert.Equal(SettingsLoadOutcome.ResetAfterCorruption, result.Outcome);
        AssertDefaults(result.Settings);
        Assert.False(File.Exists(SettingsPath));
        Assert.NotNull(result.PreservedFilePath);
        Assert.StartsWith(JsonSettingsStore.QuarantinePrefix, Path.GetFileName(result.PreservedFilePath), StringComparison.Ordinal);
        Assert.Equal(content, await File.ReadAllTextAsync(result.PreservedFilePath, Ct));
        Assert.False(store.IsReadOnly);
    }

    [Fact]
    public async Task Truncated_file_is_treated_as_corrupt()
    {
        using var store = CreateStore();
        await store.SaveAsync(new AppSettings(), Ct);
        var bytes = await File.ReadAllBytesAsync(SettingsPath, Ct);
        await File.WriteAllBytesAsync(SettingsPath, bytes[..(bytes.Length / 2)], Ct);
        File.Delete(_directory.Combine(JsonSettingsStore.BackupFileName));

        var result = await store.LoadAsync(Ct);

        Assert.Equal(SettingsLoadOutcome.ResetAfterCorruption, result.Outcome);
    }

    [Fact]
    public async Task Oversized_file_is_treated_as_corrupt()
    {
        var padding = new string('x', JsonSettingsStore.MaxFileBytes);
        await File.WriteAllTextAsync(SettingsPath, $"{{\"schemaVersion\":1,\"pad\":\"{padding}\"}}", Ct);
        using var store = CreateStore();

        var result = await store.LoadAsync(Ct);

        Assert.Equal(SettingsLoadOutcome.ResetAfterCorruption, result.Outcome);
    }

    [Fact]
    public async Task Corrupt_file_recovers_last_good_backup()
    {
        using var store = CreateStore();
        await store.SaveAsync(WithOpacity(0.5), Ct);
        await store.SaveAsync(WithOpacity(0.6), Ct);
        await File.WriteAllTextAsync(SettingsPath, "{ broken", Ct);

        var result = await store.LoadAsync(Ct);

        Assert.Equal(SettingsLoadOutcome.RecoveredFromBackup, result.Outcome);
        Assert.Equal(0.5, result.Settings.Appearance.Opacity);
        Assert.NotNull(result.PreservedFilePath);
        Assert.True(File.Exists(result.PreservedFilePath));

        var reloaded = await store.LoadAsync(Ct);
        Assert.Equal(SettingsLoadOutcome.Loaded, reloaded.Outcome);
        Assert.Equal(0.5, reloaded.Settings.Appearance.Opacity);
    }

    [Fact]
    public async Task Unknown_properties_are_ignored()
    {
        const string json = """{"schemaVersion":1,"futureFlag":true,"appearance":{"opacity":0.5,"glow":"neon"}}""";
        await File.WriteAllTextAsync(SettingsPath, json, Ct);
        using var store = CreateStore();

        var result = await store.LoadAsync(Ct);

        Assert.Equal(SettingsLoadOutcome.Loaded, result.Outcome);
        Assert.Equal(0.5, result.Settings.Appearance.Opacity);
        Assert.Empty(result.Corrections);
    }

    [Fact]
    public async Task Hand_edited_file_with_bom_comments_and_trailing_commas_loads()
    {
        const string json = """
            // edited by hand
            { "schemaVersion": 1, "appearance": { "theme": "light", }, }
            """;
        await File.WriteAllTextAsync(SettingsPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), Ct);
        using var store = CreateStore();

        var result = await store.LoadAsync(Ct);

        Assert.Equal(SettingsLoadOutcome.Loaded, result.Outcome);
        Assert.Equal(ThemeMode.Light, result.Settings.Appearance.Theme);
    }

    [Fact]
    public async Task Invalid_values_in_file_are_corrected_on_load()
    {
        const string json = """{"schemaVersion":1,"appearance":{"opacity":7,"theme":42},"behavior":{"hoverPreviewDelayMs":1}}""";
        await File.WriteAllTextAsync(SettingsPath, json, Ct);
        using var store = CreateStore();

        var result = await store.LoadAsync(Ct);

        Assert.Equal(SettingsLoadOutcome.Loaded, result.Outcome);
        Assert.Equal(SettingsValidator.MaxOpacity, result.Settings.Appearance.Opacity);
        Assert.Equal(ThemeMode.System, result.Settings.Appearance.Theme);
        Assert.Equal(SettingsValidator.MinHoverPreviewDelayMs, result.Settings.Behavior.HoverPreviewDelayMs);
        Assert.Equal(3, result.Corrections.Count);
    }

    [Fact]
    public async Task Newer_schema_is_never_overwritten()
    {
        const string json = """{"schemaVersion":99,"appearance":{"opacity":0.4}}""";
        await File.WriteAllTextAsync(SettingsPath, json, Ct);
        using var store = CreateStore();

        var result = await store.LoadAsync(Ct);
        var save = await store.SaveAsync(new AppSettings(), Ct);

        Assert.Equal(SettingsLoadOutcome.NewerVersionReadOnly, result.Outcome);
        AssertDefaults(result.Settings);
        Assert.True(store.IsReadOnly);
        Assert.Equal(SettingsSaveResult.SkippedReadOnly, save);
        Assert.Equal(json, await File.ReadAllTextAsync(SettingsPath, Ct));
        Assert.Equal([JsonSettingsStore.FileName], _directory.FileNames());
    }

    [Fact]
    public async Task Read_only_state_clears_when_reloaded_file_is_supported()
    {
        await File.WriteAllTextAsync(SettingsPath, """{"schemaVersion":99}""", Ct);
        using var store = CreateStore();
        await store.LoadAsync(Ct);
        File.Delete(SettingsPath);

        var result = await store.LoadAsync(Ct);

        Assert.Equal(SettingsLoadOutcome.CreatedDefaults, result.Outcome);
        Assert.False(store.IsReadOnly);
    }

    [Fact]
    public async Task Legacy_v0_fixture_is_backed_up_migrated_and_saved()
    {
        var fixture = await File.ReadAllBytesAsync(FixturePath("settings-v0.json"), Ct);
        await File.WriteAllBytesAsync(SettingsPath, fixture, Ct);
        using var store = CreateStore(new LegacyV0Migration());

        var result = await store.LoadAsync(Ct);

        Assert.Equal(SettingsLoadOutcome.Migrated, result.Outcome);
        Assert.Equal(ThemeMode.Dark, result.Settings.Appearance.Theme);
        Assert.Equal(0.8, result.Settings.Appearance.Opacity, precision: 10);
        Assert.Equal(500, result.Settings.Behavior.HoverPreviewDelayMs);
        Assert.Equal([WidgetIds.Notes, WidgetIds.Clock], result.Settings.Widgets.Take(2).Select(w => w.Id));
        Assert.Equal(WidgetIds.All.Count, result.Settings.Widgets.Count);

        Assert.Equal(_directory.Combine("settings.v0.backup.json"), result.PreservedFilePath);
        Assert.Equal(fixture, await File.ReadAllBytesAsync(result.PreservedFilePath!, Ct));

        var reloaded = await store.LoadAsync(Ct);
        Assert.Equal(SettingsLoadOutcome.Loaded, reloaded.Outcome);
        AssertEquivalent(result.Settings, reloaded.Settings);
    }

    [Fact]
    public async Task Legacy_file_without_migration_path_is_treated_as_corrupt()
    {
        File.Copy(FixturePath("settings-v0.json"), SettingsPath);
        using var store = CreateStore();

        var result = await store.LoadAsync(Ct);

        Assert.Equal(SettingsLoadOutcome.ResetAfterCorruption, result.Outcome);
    }

    [Fact]
    public async Task Failed_migration_leaves_file_untouched_and_disables_saving()
    {
        var fixture = await File.ReadAllBytesAsync(FixturePath("settings-v0.json"), Ct);
        await File.WriteAllBytesAsync(SettingsPath, fixture, Ct);
        using var store = CreateStore(new ThrowingMigration());

        var result = await store.LoadAsync(Ct);
        var save = await store.SaveAsync(new AppSettings(), Ct);

        Assert.Equal(SettingsLoadOutcome.MigrationFailedReadOnly, result.Outcome);
        Assert.True(store.IsReadOnly);
        Assert.Equal(SettingsSaveResult.SkippedReadOnly, save);
        Assert.Equal(fixture, await File.ReadAllBytesAsync(SettingsPath, Ct));
        Assert.True(File.Exists(result.PreservedFilePath));
    }

    [Fact]
    public async Task Concurrent_saves_are_serialized_and_leave_a_valid_file()
    {
        using var store = CreateStore();
        var opacities = Enumerable.Range(0, 25).Select(i => 0.5 + (i / 100.0)).ToArray();

        await Task.WhenAll(opacities.Select(o => Task.Run(() => store.SaveAsync(WithOpacity(o), Ct), Ct)));

        var result = await store.LoadAsync(Ct);
        Assert.Equal(SettingsLoadOutcome.Loaded, result.Outcome);
        Assert.Contains(result.Settings.Appearance.Opacity, opacities);
        Assert.Equal([JsonSettingsStore.FileName, JsonSettingsStore.BackupFileName], _directory.FileNames());
    }

    [Fact]
    public async Task Quarantine_keeps_only_newest_copies()
    {
        using var store = CreateStore();

        for (var i = 0; i < JsonSettingsStore.MaxQuarantinedFiles + 3; i++)
        {
            await File.WriteAllTextAsync(SettingsPath, "{", Ct);
            await store.LoadAsync(Ct);
        }

        var quarantined = _directory.FileNames().Where(f => f.StartsWith(JsonSettingsStore.QuarantinePrefix, StringComparison.Ordinal));
        Assert.Equal(JsonSettingsStore.MaxQuarantinedFiles, quarantined.Count());
    }

    [Fact]
    public void Invalid_migration_registrations_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => CreateStore(new LegacyV0Migration(), new LegacyV0Migration()));
        Assert.Throws<ArgumentException>(() => CreateStore(new FixedVersionMigration(AppSettings.CurrentSchemaVersion)));
        Assert.Throws<ArgumentException>(() => CreateStore(new FixedVersionMigration(-1)));
    }

    private JsonSettingsStore CreateStore(params ISettingsMigration[] migrations) =>
        new(new SettingsStoreOptions(_directory.Path) { Migrations = migrations }, new FixedTimeProvider(Now));

    private static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "Settings", "Fixtures", name);

    private static AppSettings WithOpacity(double opacity) =>
        new() { Appearance = new AppearanceSettings { Opacity = opacity } };

    private static void AssertDefaults(AppSettings actual) => AssertEquivalent(new AppSettings(), actual);

    private static void AssertEquivalent(AppSettings expected, AppSettings actual)
    {
        Assert.Equal(expected.SchemaVersion, actual.SchemaVersion);
        Assert.Equal(expected.General, actual.General);
        Assert.Equal(expected.Appearance, actual.Appearance);
        Assert.Equal(expected.Behavior, actual.Behavior);
        Assert.Equal(expected.Updates, actual.Updates);
        Assert.Equal(expected.Widgets, actual.Widgets);
    }

    /// <summary>Upgrades the hypothetical flat v0 shape in <c>Fixtures/settings-v0.json</c>.</summary>
    private sealed class LegacyV0Migration : ISettingsMigration
    {
        public int FromVersion => 0;

        public JsonObject Migrate(JsonObject document)
        {
            var widgets = new JsonArray();
            foreach (var id in document["widgets"]?.AsArray() ?? new JsonArray())
            {
                widgets.Add(new JsonObject { ["id"] = id?.GetValue<string>(), ["enabled"] = true });
            }

            return new JsonObject
            {
                ["appearance"] = new JsonObject
                {
                    ["theme"] = document["theme"]?.GetValue<string>(),
                    ["opacity"] = document["opacityPercent"]?.GetValue<double>() / 100,
                },
                ["behavior"] = new JsonObject { ["hoverPreviewDelayMs"] = document["hoverDelay"]?.GetValue<int>() },
                ["widgets"] = widgets,
            };
        }
    }

    private sealed class ThrowingMigration : ISettingsMigration
    {
        public int FromVersion => 0;

        public JsonObject Migrate(JsonObject document) => throw new InvalidOperationException("Simulated migration bug.");
    }

    private sealed class FixedVersionMigration(int fromVersion) : ISettingsMigration
    {
        public int FromVersion => fromVersion;

        public JsonObject Migrate(JsonObject document) => document;
    }
}
