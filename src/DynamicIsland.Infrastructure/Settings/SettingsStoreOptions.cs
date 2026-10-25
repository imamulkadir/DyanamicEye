namespace DynamicIsland.Infrastructure.Settings;

/// <param name="DataDirectory">Folder holding <c>settings.json</c>; normally <see cref="AppDataPaths.Data"/>.</param>
public sealed record SettingsStoreOptions(string DataDirectory)
{
    /// <summary>Ordered upgrade steps. Schema v1 is the first released schema, so production has none yet.</summary>
    public IReadOnlyList<ISettingsMigration> Migrations { get; init; } = [];
}
