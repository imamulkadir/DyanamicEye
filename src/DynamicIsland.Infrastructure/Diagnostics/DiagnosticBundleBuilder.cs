using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DynamicIsland.Core.Components;
using DynamicIsland.Core.Diagnostics;
using DynamicIsland.Infrastructure.Lifecycle;
using DynamicIsland.Infrastructure.Logging;

namespace DynamicIsland.Infrastructure.Diagnostics;

/// <summary>What the app knows about itself at the time diagnostics are requested. Names and codes only.</summary>
public sealed record DiagnosticContext(
    string AppVersion,
    bool SafeMode,
    string? SettingsOutcome,
    IReadOnlyList<ComponentStatus> Components);

public sealed record DiagnosticFile(string Name, string Content)
{
    public int Bytes => Encoding.UTF8.GetByteCount(Content);
}

/// <summary>Exactly what would be exported; the user reviews this before anything is written (D14).</summary>
public sealed record DiagnosticPreview(DateTimeOffset CreatedUtc, IReadOnlyList<DiagnosticFile> Files)
{
    public long TotalBytes => Files.Sum(f => (long)f.Bytes);
}

/// <summary>
/// Builds a local, user-reviewed diagnostic bundle (guide D14, §4.5). Everything is redacted again, settings values
/// are never included, and <see cref="WriteZipAsync"/> writes only the files of an approved preview. Nothing is uploaded.
/// </summary>
public sealed class DiagnosticBundleBuilder(string logsDirectory, string dataDirectory, LogRedactor redactor, TimeProvider timeProvider)
{
    public const int MaxLogLines = 500;
    public const int MaxFileChars = 512 * 1024;
    public const string SummaryFile = "summary.txt";
    public const string AppLogFile = "app-log.txt";
    public const string LauncherLogFile = "launcher-log.txt";
    public const string ManifestFile = "manifest.json";

    public DiagnosticPreview BuildPreview(DiagnosticContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var now = timeProvider.GetUtcNow();
        return new DiagnosticPreview(now,
        [
            new DiagnosticFile(SummaryFile, Bound(redactor.Redact(BuildSummary(context, now)))),
            new DiagnosticFile(AppLogFile, Bound(redactor.Redact(ReadLogTail(FileLoggerOptions.DefaultFilePrefix)))),
            new DiagnosticFile(LauncherLogFile, Bound(redactor.Redact(ReadLogTail("launcher-")))),
        ]);
    }

    /// <summary>Writes the approved preview, plus a manifest of names, sizes and SHA-256 hashes, to a new zip file.</summary>
    /// <exception cref="IOException">The destination already exists or cannot be written.</exception>
    public static async Task WriteZipAsync(DiagnosticPreview approvedPreview, string destinationPath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(approvedPreview);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var stream = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await using (stream.ConfigureAwait(false))
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
            var manifest = new List<object>();
            foreach (var file in approvedPreview.Files)
            {
                var bytes = Encoding.UTF8.GetBytes(file.Content);
                await WriteEntryAsync(archive, file.Name, bytes, cancellationToken).ConfigureAwait(false);
                manifest.Add(new { name = file.Name, bytes = bytes.Length, sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)) });
            }

            var manifestJson = JsonSerializer.SerializeToUtf8Bytes(new
            {
                createdUtc = approvedPreview.CreatedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                files = manifest,
            });
            await WriteEntryAsync(archive, ManifestFile, manifestJson, cancellationToken).ConfigureAwait(false);
        }
    }

    private string BuildSummary(DiagnosticContext context, DateTimeOffset now)
    {
        var crashes = new JsonCrashStateStore(dataDirectory).ReadRecentCrashes(now);
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"Created (UTC): {now.UtcDateTime:O}");
        text.AppendLine(CultureInfo.InvariantCulture, $"App version: {context.AppVersion}");
        text.AppendLine(CultureInfo.InvariantCulture, $"OS: {Environment.OSVersion.VersionString} ({RuntimeInformation.OSArchitecture})");
        text.AppendLine(CultureInfo.InvariantCulture, $".NET: {RuntimeInformation.FrameworkDescription}");
        text.AppendLine(CultureInfo.InvariantCulture, $"Safe Mode: {context.SafeMode}");
        text.AppendLine(CultureInfo.InvariantCulture, $"Settings load outcome: {context.SettingsOutcome ?? "unknown"}");
        text.AppendLine(CultureInfo.InvariantCulture, $"Recent crashes (last 5 min): {crashes.Count}");
        foreach (var crash in crashes)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"  {crash.UtcDateTime:O}");
        }

        text.AppendLine("Components:");
        foreach (var component in context.Components)
        {
            text.AppendLine(CultureInfo.InvariantCulture,
                $"  {component.Name}: {component.State} reason={component.ReasonCode ?? "-"} failures={component.ConsecutiveFailures} error={component.LastErrorType ?? "-"}");
        }

        return text.ToString();
    }

    /// <summary>Last <see cref="MaxLogLines"/> lines across the newest log files with the given prefix.</summary>
    private string ReadLogTail(string prefix)
    {
        if (!Directory.Exists(logsDirectory))
        {
            return string.Empty;
        }

        var lines = new List<string>();
        var files = Directory.GetFiles(logsDirectory, prefix + "*" + FileLoggerOptions.FileExtension)
            .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
            .ThenByDescending(f => f, StringComparer.Ordinal);

        foreach (var file in files)
        {
            var fileLines = ReadAllLinesShared(file);
            lines.InsertRange(0, fileLines.TakeLast(MaxLogLines - lines.Count));
            if (lines.Count >= MaxLogLines)
            {
                break;
            }
        }

        return string.Join('\n', lines);
    }

    private static List<string> ReadAllLinesShared(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var lines = new List<string>();
            while (reader.ReadLine() is { } line)
            {
                lines.Add(line);
            }

            return lines;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [$"[log file unavailable: {ex.GetType().Name}]"];
        }
    }

    private static string Bound(string content) =>
        content.Length <= MaxFileChars ? content : string.Concat(content.AsSpan(content.Length - MaxFileChars), "\n[earlier content truncated]");

    private static async Task WriteEntryAsync(ZipArchive archive, string name, byte[] bytes, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        var entryStream = await entry.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using (entryStream.ConfigureAwait(false))
        {
            await entryStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        }
    }
}
