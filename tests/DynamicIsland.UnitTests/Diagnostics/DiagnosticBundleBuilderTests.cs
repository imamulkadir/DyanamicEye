using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DynamicIsland.Core.Components;
using DynamicIsland.Core.Diagnostics;
using DynamicIsland.Infrastructure.Diagnostics;
using DynamicIsland.Infrastructure.Lifecycle;
using DynamicIsland.UnitTests.TestSupport;

namespace DynamicIsland.UnitTests.Diagnostics;

public sealed class DiagnosticBundleBuilderTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly LogRedactor Redactor = new([(@"C:\Users\alice", "%USERPROFILE%"), ("alice", "[USER]"), ("DESKTOP-42", "[MACHINE]")]);

    private static readonly DiagnosticContext Context = new(
        "0.1.0",
        SafeMode: true,
        "RecoveredFromBackup",
        [new ComponentStatus("media", ComponentState.Failed, "recoverable-failure", 4, null, "System.InvalidOperationException")]);

    private readonly TempDirectory _root = new();

    public DiagnosticBundleBuilderTests()
    {
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Data);
    }

    private string Logs => _root.Combine("logs");

    private string Data => _root.Combine("data");

    public void Dispose() => _root.Dispose();

    [Fact]
    public void Preview_contains_summary_and_both_log_tails_without_personal_data()
    {
        // Simulates older or third-party lines that were never redacted.
        File.WriteAllLines(Path.Combine(Logs, "dynamicisland-20261025.log"),
        [
            @"{""message"":""Opened C:\\Users\\alice\\notes.db on DESKTOP-42""}",
            "user alice token=ghp_0123456789abcdefghijABCDEFGHIJ0123 mail alice@example.com",
        ]);
        File.WriteAllLines(Path.Combine(Logs, "launcher-20261025.log"), ["launcher run by alice"]);
        new JsonCrashStateStore(Data).RecordCrash(Now.AddMinutes(-1));

        var preview = CreateBuilder().BuildPreview(Context);

        Assert.Equal([DiagnosticBundleBuilder.SummaryFile, DiagnosticBundleBuilder.AppLogFile, DiagnosticBundleBuilder.LauncherLogFile], preview.Files.Select(f => f.Name));
        var all = string.Join('\n', preview.Files.Select(f => f.Content));
        Assert.DoesNotContain("alice", all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DESKTOP-42", all, StringComparison.Ordinal);
        Assert.DoesNotContain("ghp_", all, StringComparison.Ordinal);

        var summary = preview.Files[0].Content;
        Assert.Contains("Safe Mode: True", summary, StringComparison.Ordinal);
        Assert.Contains("Settings load outcome: RecoveredFromBackup", summary, StringComparison.Ordinal);
        Assert.Contains("Recent crashes (last 5 min): 1", summary, StringComparison.Ordinal);
        Assert.Contains("media: Failed reason=recoverable-failure failures=4 error=System.InvalidOperationException", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Log_tail_is_limited_to_the_newest_lines()
    {
        File.WriteAllLines(Path.Combine(Logs, "dynamicisland-20261024.log"), Enumerable.Range(0, 400).Select(i => $"old {i}"));
        File.SetLastWriteTimeUtc(Path.Combine(Logs, "dynamicisland-20261024.log"), Now.UtcDateTime.AddDays(-1));
        File.WriteAllLines(Path.Combine(Logs, "dynamicisland-20261025.log"), Enumerable.Range(0, 300).Select(i => $"new {i}"));

        var lines = CreateBuilder().BuildPreview(Context).Files[1].Content.Split('\n');

        Assert.Equal(DiagnosticBundleBuilder.MaxLogLines, lines.Length);
        Assert.Equal("old 200", lines[0]);
        Assert.Equal("new 299", lines[^1]);
    }

    [Fact]
    public void Missing_logs_produce_empty_files_not_errors()
    {
        Directory.Delete(Logs);

        var preview = CreateBuilder().BuildPreview(Context);

        Assert.Equal(string.Empty, preview.Files[1].Content);
    }

    [Fact]
    public async Task Zip_contains_exactly_the_previewed_files_and_a_matching_manifest()
    {
        File.WriteAllLines(Path.Combine(Logs, "dynamicisland-20261025.log"), ["entry"]);
        var preview = CreateBuilder().BuildPreview(Context);
        var zipPath = _root.Combine("bundle.zip");

        await DiagnosticBundleBuilder.WriteZipAsync(preview, zipPath, TestContext.Current.CancellationToken);

        using var archive = ZipFile.OpenRead(zipPath);
        Assert.Equal(
            preview.Files.Select(f => f.Name).Append(DiagnosticBundleBuilder.ManifestFile),
            archive.Entries.Select(e => e.FullName));

        foreach (var file in preview.Files)
        {
            using var reader = new StreamReader(archive.GetEntry(file.Name)!.Open(), Encoding.UTF8);
            Assert.Equal(file.Content, await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
        }

        using var manifest = JsonDocument.Parse(archive.GetEntry(DiagnosticBundleBuilder.ManifestFile)!.Open());
        var entries = manifest.RootElement.GetProperty("files").EnumerateArray().ToArray();
        Assert.Equal(preview.Files.Count, entries.Length);
        for (var i = 0; i < entries.Length; i++)
        {
            var expectedHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(preview.Files[i].Content)));
            Assert.Equal(expectedHash, entries[i].GetProperty("sha256").GetString());
        }
    }

    [Fact]
    public async Task Existing_destination_is_never_overwritten()
    {
        var zipPath = _root.Combine("bundle.zip");
        await File.WriteAllTextAsync(zipPath, "keep me", TestContext.Current.CancellationToken);
        var preview = CreateBuilder().BuildPreview(Context);

        await Assert.ThrowsAsync<IOException>(() => DiagnosticBundleBuilder.WriteZipAsync(preview, zipPath, TestContext.Current.CancellationToken));

        Assert.Equal("keep me", await File.ReadAllTextAsync(zipPath, TestContext.Current.CancellationToken));
    }

    private DiagnosticBundleBuilder CreateBuilder() => new(Logs, Data, Redactor, new FixedTimeProvider(Now));
}
