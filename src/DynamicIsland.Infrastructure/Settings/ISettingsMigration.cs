using System.Text.Json.Nodes;

namespace DynamicIsland.Infrastructure.Settings;

/// <summary>
/// Upgrades the raw settings document from <see cref="FromVersion"/> to <c>FromVersion + 1</c>.
/// Works on JSON so old shapes need no CLR types. The store stamps the new <c>schemaVersion</c> afterwards.
/// </summary>
public interface ISettingsMigration
{
    int FromVersion { get; }

    JsonObject Migrate(JsonObject document);
}
