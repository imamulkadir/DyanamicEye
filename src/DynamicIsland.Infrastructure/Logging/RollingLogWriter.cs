using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Channels;
using DynamicIsland.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace DynamicIsland.Infrastructure.Logging;

internal readonly record struct LogEntry(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Category,
    EventId EventId,
    string Message,
    string? Exception);

/// <summary>
/// Single background consumer writing <c>dynamicisland-yyyyMMdd[.N].log</c>. One JSON object per line.
/// Retention and the total size cap are applied whenever a new file is opened.
/// </summary>
internal sealed class RollingLogWriter : IDisposable
{
    private const string TruncatedSuffix = "…[truncated]";
    private static readonly TimeSpan DisposeTimeout = TimeSpan.FromSeconds(3);
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly JsonWriterOptions JsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly FileLoggerOptions _options;
    private readonly LogRedactor _redactor;
    private readonly TimeProvider _timeProvider;
    private readonly Channel<LogEntry> _channel;
    private readonly Task _pump;

    private long _dropped;
    private volatile bool _disabled;
    private StreamWriter? _stream;
    private string? _currentPath;
    private DateOnly _currentDay;
    private long _currentBytes;

    // startGate is a test hook: the writer waits for it before consuming entries.
    public RollingLogWriter(FileLoggerOptions options, LogRedactor redactor, TimeProvider timeProvider, Task? startGate = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(redactor);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Directory);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.QueueCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxFileBytes, 1024);

        _options = options with { Directory = Path.GetFullPath(options.Directory) };
        _redactor = redactor;
        _timeProvider = timeProvider;

        var channelOptions = new BoundedChannelOptions(options.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        };
        _channel = Channel.CreateBounded<LogEntry>(channelOptions, _ => Interlocked.Increment(ref _dropped));
        _pump = Task.Run(() => PumpAsync(startGate ?? Task.CompletedTask));
    }

    public bool IsEnabled => !_disabled;

    public DateTimeOffset UtcNow => _timeProvider.GetUtcNow();

    public void Enqueue(LogEntry entry)
    {
        if (!_disabled)
        {
            _channel.Writer.TryWrite(entry);
        }
    }

    public void Dispose()
    {
        _channel.Writer.TryComplete();
        _pump.Wait(DisposeTimeout);
    }

    private async Task PumpAsync(Task startGate)
    {
        await startGate.ConfigureAwait(false);

        var reader = _channel.Reader;
        while (await reader.WaitToReadAsync().ConfigureAwait(false))
        {
            while (reader.TryRead(out var entry))
            {
                Write(entry);
            }

            Flush();
        }

        CloseFile();
    }

    private void Write(LogEntry entry)
    {
        if (_disabled)
        {
            return;
        }

        try
        {
            var dropped = Interlocked.Exchange(ref _dropped, 0);
            if (dropped > 0)
            {
                var notice = $"{dropped.ToString(CultureInfo.InvariantCulture)} log entries were dropped because the log queue was full.";
                WriteLine(new LogEntry(entry.Timestamp, LogLevel.Warning, typeof(RollingLogWriter).FullName!, default, notice, null));
            }

            WriteLine(entry);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Disable();
        }
    }

    private void WriteLine(LogEntry entry)
    {
        var line = Format(entry);
        var bytes = Utf8NoBom.GetByteCount(line) + 1;
        EnsureFile(entry.Timestamp, bytes);
        _stream!.Write(line);
        _stream.Write('\n');
        _currentBytes += bytes;
    }

    private void EnsureFile(DateTimeOffset timestamp, long nextBytes)
    {
        var day = DateOnly.FromDateTime(timestamp.UtcDateTime);
        var needsRoll = _currentBytes > 0 && _currentBytes + nextBytes > _options.MaxFileBytes;
        if (_stream is not null && day == _currentDay && !needsRoll)
        {
            return;
        }

        CloseFile();
        Directory.CreateDirectory(_options.Directory);

        (_currentPath, _currentBytes) = SelectFile(day, nextBytes);
        var file = new FileStream(_currentPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        _stream = new StreamWriter(file, Utf8NoBom);
        _currentDay = day;

        ApplyRetention();
    }

    private (string Path, long Bytes) SelectFile(DateOnly day, long nextBytes)
    {
        var baseName = FileLoggerOptions.FilePrefix + day.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        for (var index = 0; ; index++)
        {
            var suffix = index == 0 ? string.Empty : "." + index.ToString(CultureInfo.InvariantCulture);
            var path = Path.Combine(_options.Directory, baseName + suffix + FileLoggerOptions.FileExtension);
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return (path, 0);
            }

            if (info.Length == 0 || info.Length + nextBytes <= _options.MaxFileBytes)
            {
                return (path, info.Length);
            }
        }
    }

    private void ApplyRetention()
    {
        var cutoff = _timeProvider.GetUtcNow().UtcDateTime - _options.Retention;
        var files = new DirectoryInfo(_options.Directory)
            .GetFiles(FileLoggerOptions.FilePrefix + "*" + FileLoggerOptions.FileExtension)
            .Where(f => !string.Equals(f.FullName, _currentPath, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.LastWriteTimeUtc)
            .ToList();

        var kept = new List<FileInfo>(files.Count);
        foreach (var file in files)
        {
            if (file.LastWriteTimeUtc >= cutoff || !TryDelete(file))
            {
                kept.Add(file);
            }
        }

        var total = _currentBytes + kept.Sum(f => f.Length);
        foreach (var file in kept)
        {
            if (total <= _options.MaxTotalBytes)
            {
                break;
            }

            // Read the size first: FileInfo.Delete() invalidates the cached length.
            var length = file.Length;
            if (TryDelete(file))
            {
                total -= length;
            }
        }
    }

    private string Format(LogEntry entry)
    {
        var message = _redactor.Redact(entry.Message);
        if (message.Length > _options.MaxMessageChars)
        {
            message = string.Concat(message.AsSpan(0, _options.MaxMessageChars), TruncatedSuffix);
        }

        var buffer = new ArrayBufferWriter<byte>(256);
        using (var json = new Utf8JsonWriter(buffer, JsonOptions))
        {
            json.WriteStartObject();
            json.WriteString("time", entry.Timestamp.ToUniversalTime());
            json.WriteString("level", entry.Level.ToString());
            json.WriteString("category", entry.Category);
            if (entry.EventId.Id != 0)
            {
                json.WriteNumber("eventId", entry.EventId.Id);
            }

            if (!string.IsNullOrEmpty(entry.EventId.Name))
            {
                json.WriteString("event", entry.EventId.Name);
            }

            json.WriteString("message", message);
            if (entry.Exception is not null)
            {
                json.WriteString("exception", _redactor.Redact(entry.Exception));
            }

            json.WriteEndObject();
        }

        return Utf8NoBom.GetString(buffer.WrittenSpan);
    }

    private void Flush()
    {
        try
        {
            _stream?.Flush();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Disable();
        }
    }

    private void Disable()
    {
        _disabled = true;
        CloseFile();
    }

    private void CloseFile()
    {
        var stream = _stream;
        _stream = null;
        if (stream is null)
        {
            return;
        }

        try
        {
            stream.Dispose();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _disabled = true;
        }
    }

    private static bool TryDelete(FileInfo file)
    {
        try
        {
            file.Delete();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Locked by a viewer or AV scan; retried at the next roll.
            return false;
        }
    }
}
