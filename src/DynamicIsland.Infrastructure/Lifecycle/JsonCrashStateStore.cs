using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using DynamicIsland.Core.Lifecycle;

namespace DynamicIsland.Infrastructure.Lifecycle;

/// <summary>
/// Crash times in <c>data/crash-history.json</c> (<c>{"schemaVersion":1,"crashes":["…"]}</c>).
/// Unreadable, corrupt or unwritable files never stop the launcher: the history falls back to memory for this run.
/// </summary>
public sealed class JsonCrashStateStore(string dataDirectory) : ICrashStateStore
{
    public const string FileName = "crash-history.json";
    private const int SchemaVersion = 1;
    private const int MaxEntries = 10;
    private const int MaxFileBytes = 16 * 1024;

    private readonly string _path = Path.Combine(Path.GetFullPath(dataDirectory), FileName);
    private List<DateTimeOffset> _fallback = [];

    public IReadOnlyList<DateTimeOffset> RecordCrash(DateTimeOffset now)
    {
        // Merge with what this run already knows, so an unreadable or unwritable file never loses crashes.
        var crashes = (TryLoad() ?? []).Union(_fallback).ToList();
        crashes.Add(now);

        var recent = crashes
            .Where(t => CrashPolicy.IsRecent(t, now))
            .Order()
            .TakeLast(MaxEntries)
            .ToList();

        _fallback = recent;
        TrySave(recent);
        return recent;
    }

    private List<DateTimeOffset>? TryLoad()
    {
        try
        {
            var info = new FileInfo(_path);
            if (!info.Exists)
            {
                return [];
            }

            if (info.Length > MaxFileBytes)
            {
                return null;
            }

            var crashes = JsonNode.Parse(File.ReadAllBytes(_path))?["crashes"]?.AsArray();
            return crashes?
                .Select(n => n is JsonValue v && v.TryGetValue(out string? s)
                    && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t) ? t : (DateTimeOffset?)null)
                .OfType<DateTimeOffset>()
                .ToList() ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private void TrySave(IReadOnlyList<DateTimeOffset> crashes)
    {
        var document = new JsonObject
        {
            ["schemaVersion"] = SchemaVersion,
            ["crashes"] = new JsonArray(crashes.Select(t => (JsonNode)t.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)).ToArray()),
        };

        var tempPath = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(tempPath, document.ToJsonString());
            File.Move(tempPath, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // History stays in memory for this launcher run.
        }
        finally
        {
            try
            {
                File.Delete(tempPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best effort.
            }
        }
    }
}
