using System.Text.Json;
using DynamicIsland.Core.Diagnostics;
using DynamicIsland.Infrastructure.Logging;
using DynamicIsland.UnitTests.TestSupport;
using Microsoft.Extensions.Logging;

namespace DynamicIsland.UnitTests.Logging;

public sealed class FileLoggerProviderTests : IDisposable
{
    private const string Category = "Tests.Category";
    private static readonly DateTimeOffset Day1 = new(2026, 10, 25, 9, 30, 0, TimeSpan.Zero);
    private static readonly LogRedactor Redactor = new([(@"C:\Users\alice", "%USERPROFILE%"), ("alice", "[USER]")]);

    private readonly TempDirectory _directory = new();
    private readonly FixedTimeProvider _time = new(Day1);

    public void Dispose() => _directory.Dispose();

    [Fact]
    public void Entry_is_one_redacted_json_line()
    {
        using (var provider = CreateProvider())
        {
            provider.CreateLogger(Category).Log(LogLevel.Warning, new EventId(42, "Probe"), @"Opened C:\Users\alice\x.json for alice", null, (s, _) => s);
        }

        var line = Assert.Single(ReadAllLines());
        using var json = JsonDocument.Parse(line);
        var root = json.RootElement;
        Assert.Equal("Warning", root.GetProperty("level").GetString());
        Assert.Equal(Category, root.GetProperty("category").GetString());
        Assert.Equal(42, root.GetProperty("eventId").GetInt32());
        Assert.Equal("Probe", root.GetProperty("event").GetString());
        Assert.Equal(@"Opened %USERPROFILE%\x.json for [USER]", root.GetProperty("message").GetString());
        Assert.Equal(Day1, root.GetProperty("time").GetDateTimeOffset());
        Assert.DoesNotContain("alice", line, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Exception_type_and_stack_are_written_without_the_message()
    {
        using (var provider = CreateProvider())
        {
            var exception = Throw(new InvalidOperationException(@"Cannot open D:\Private\taxes-backup.zip"));
            provider.CreateLogger(Category).LogError(exception, "Operation failed");
        }

        using var json = JsonDocument.Parse(Assert.Single(ReadAllLines()));
        var text = json.RootElement.GetProperty("exception").GetString();
        Assert.NotNull(text);
        Assert.Contains("System.InvalidOperationException (HResult 0x80131509)", text, StringComparison.Ordinal);
        Assert.Contains(nameof(Throw), text, StringComparison.Ordinal);
        Assert.DoesNotContain("taxes-backup", text, StringComparison.Ordinal); // Messages can name personal files (P07-01).
        Assert.DoesNotContain("Cannot open", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Inner_and_aggregated_exceptions_are_described_by_type()
    {
        var exception = new AggregateException(
            new IOException("secret-a", Throw(new UnauthorizedAccessException("secret-b"))),
            new FormatException("secret-c"));

        var text = ExceptionText.Describe(exception);

        Assert.Contains("System.AggregateException", text, StringComparison.Ordinal);
        Assert.Contains(" ---> System.IO.IOException", text, StringComparison.Ordinal);
        Assert.Contains(" ---> System.UnauthorizedAccessException", text, StringComparison.Ordinal);
        Assert.Contains(" ---> System.FormatException", text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Entries_below_minimum_level_are_not_written()
    {
        using (var provider = CreateProvider())
        {
            var logger = provider.CreateLogger(Category);
            Assert.False(logger.IsEnabled(LogLevel.Debug));
            logger.LogDebug("hidden");
            logger.LogInformation("shown");
        }

        Assert.Single(ReadAllLines());
    }

    [Fact]
    public void Long_messages_are_truncated()
    {
        using (var provider = CreateProvider(o => o with { MaxMessageChars = 100 }))
        {
            provider.CreateLogger(Category).LogInformation("{Text}", new string('x', 5000));
        }

        using var json = JsonDocument.Parse(Assert.Single(ReadAllLines()));
        Assert.Equal(100 + "…[truncated]".Length, json.RootElement.GetProperty("message").GetString()!.Length);
    }

    [Fact]
    public void Dispose_flushes_every_queued_entry()
    {
        using (var provider = CreateProvider())
        {
            var logger = provider.CreateLogger(Category);
            for (var i = 0; i < 500; i++)
            {
                logger.LogInformation("Entry {Index}", i);
            }
        }

        Assert.Equal(500, ReadAllLines().Length);
    }

    [Fact]
    public void Files_roll_when_size_limit_is_reached()
    {
        const int maxBytes = 2048;
        using (var provider = CreateProvider(o => o with { MaxFileBytes = maxBytes }))
        {
            var logger = provider.CreateLogger(Category);
            for (var i = 0; i < 40; i++)
            {
                logger.LogInformation("{Payload}", new string('p', 200));
            }
        }

        var files = LogFiles();
        Assert.True(files.Length > 1, "Expected more than one log file.");
        Assert.Contains("dynamicisland-20261025.log", files.Select(Path.GetFileName));
        Assert.Contains("dynamicisland-20261025.1.log", files.Select(Path.GetFileName));
        Assert.All(files, f => Assert.True(new FileInfo(f).Length <= maxBytes, $"{f} exceeds the size limit."));
        Assert.Equal(40, ReadAllLines().Length);
    }

    [Fact]
    public async Task New_utc_day_starts_a_new_file()
    {
        using (var provider = CreateProvider())
        {
            var logger = provider.CreateLogger(Category);
            logger.LogInformation("day one");
            await WaitForLinesAsync(1);
            _time.UtcNow = Day1.AddDays(1);
            logger.LogInformation("day two");
        }

        Assert.Equal(["dynamicisland-20261025.log", "dynamicisland-20261026.log"], LogFiles().Select(Path.GetFileName));
    }

    [Fact]
    public void Retention_deletes_only_files_older_than_fourteen_days()
    {
        var old = CreateLogFile("dynamicisland-20261001.log", 100, Day1.AddDays(-20));
        var recent = CreateLogFile("dynamicisland-20261020.log", 100, Day1.AddDays(-5));
        var unrelated = CreateLogFile("notes.log", 100, Day1.AddDays(-60));

        using (var provider = CreateProvider())
        {
            provider.CreateLogger(Category).LogInformation("trigger retention");
        }

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
        Assert.True(File.Exists(unrelated));
    }

    [Fact]
    public void Total_size_cap_deletes_oldest_files_first()
    {
        var oldest = CreateLogFile("dynamicisland-20261021.log", 600, Day1.AddDays(-4));
        var middle = CreateLogFile("dynamicisland-20261022.log", 600, Day1.AddDays(-3));
        var newest = CreateLogFile("dynamicisland-20261023.log", 600, Day1.AddDays(-2));

        // Current file (~150 bytes) + 3 x 600 exceeds 1000: the two oldest must go, the newest fits.
        using (var provider = CreateProvider(o => o with { MaxTotalBytes = 1000 }))
        {
            provider.CreateLogger(Category).LogInformation("trigger cap");
        }

        Assert.False(File.Exists(oldest));
        Assert.False(File.Exists(middle));
        Assert.True(File.Exists(newest));
    }

    [Fact]
    public void Queue_overflow_drops_oldest_entries_and_reports_the_count()
    {
        var gate = new TaskCompletionSource();
        using (var provider = new FileLoggerProvider(Options() with { QueueCapacity = 10 }, Redactor, _time, gate.Task))
        {
            var logger = provider.CreateLogger(Category);
            for (var i = 0; i < 15; i++)
            {
                logger.LogInformation("Entry {Index}", i);
            }

            gate.SetResult();
        }

        var messages = ReadAllLines().Select(l => JsonDocument.Parse(l).RootElement.GetProperty("message").GetString()).ToArray();
        Assert.Equal(11, messages.Length);
        Assert.Equal("5 log entries were dropped because the log queue was full.", messages[0]);
        Assert.Equal("Entry 5", messages[1]);
        Assert.Equal("Entry 14", messages[^1]);
    }

    [Fact]
    public void Unwritable_folder_disables_the_sink_without_throwing()
    {
        var blocker = _directory.Combine("blocked");
        File.WriteAllText(blocker, "a file where the log folder should be");
        var provider = new FileLoggerProvider(new FileLoggerOptions(Path.Combine(blocker, "logs")), Redactor, _time);
        var logger = provider.CreateLogger(Category);

        logger.LogInformation("first");
        provider.Dispose();

        Assert.False(logger.IsEnabled(LogLevel.Information));
        logger.LogInformation("ignored after disable");
        Assert.Equal(["blocked"], _directory.FileNames());
    }

    private FileLoggerOptions Options() => new(_directory.Path);

    private FileLoggerProvider CreateProvider(Func<FileLoggerOptions, FileLoggerOptions>? configure = null) =>
        new((configure ?? (o => o))(Options()), Redactor, _time);

    private string[] LogFiles() =>
        Directory.GetFiles(_directory.Path, FileLoggerOptions.DefaultFilePrefix + "*" + FileLoggerOptions.FileExtension)
            .Order(StringComparer.Ordinal)
            .ToArray();

    /// <summary>Log lines written by the sink (pre-seeded retention files start with "seed").</summary>
    private string[] ReadAllLines() =>
        LogFiles()
            .SelectMany(ReadLinesShared)
            .Where(l => l.Length > 0 && !l.StartsWith("seed", StringComparison.Ordinal))
            .ToArray();

    private static IEnumerable<string> ReadLinesShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        return lines;
    }

    private async Task WaitForLinesAsync(int count)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (ReadAllLines().Length < count && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }

    private string CreateLogFile(string name, int bytes, DateTimeOffset lastWriteUtc)
    {
        var path = _directory.Combine(name);
        File.WriteAllText(path, "seed" + new string('s', bytes - 4));
        File.SetLastWriteTimeUtc(path, lastWriteUtc.UtcDateTime);
        return path;
    }

    private static Exception Throw(Exception exception)
    {
        try
        {
            throw exception;
        }
        catch (Exception caught)
        {
            return caught;
        }
    }
}
