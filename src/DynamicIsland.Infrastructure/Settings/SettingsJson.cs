using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using DynamicIsland.Core.Settings;

namespace DynamicIsland.Infrastructure.Settings;

/// <summary>
/// Serializer contract for <c>settings.json</c>.
/// Reflection-based on purpose: the source generator assigns every init-only property, so values missing from
/// the file would become <c>null</c>/<c>0</c> instead of keeping the defaults declared on the records.
/// </summary>
internal static class SettingsJson
{
    public static JsonTypeInfo<AppSettings> AppSettingsTypeInfo { get; } = CreateTypeInfo();

    private static JsonTypeInfo<AppSettings> CreateTypeInfo()
    {
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            Converters = { new JsonStringEnumConverter() },
        };
        options.MakeReadOnly();

        return (JsonTypeInfo<AppSettings>)options.GetTypeInfo(typeof(AppSettings));
    }
}
