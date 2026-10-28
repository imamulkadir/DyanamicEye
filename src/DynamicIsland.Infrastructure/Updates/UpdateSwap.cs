using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using DynamicIsland.Core.Distribution;
using DynamicIsland.Core.Updates;
using Microsoft.Extensions.Logging;

namespace DynamicIsland.Infrastructure.Updates;

/// <summary>Process operations the updater needs (real implementation in the Updater project; fakes in tests).</summary>
public interface IUpdaterProcesses
{
    /// <returns><c>true</c> when the process has exited (or never existed) within <paramref name="timeout"/>.</returns>
    Task<bool> WaitForExitAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken);

    /// <returns>The started launcher's process id.</returns>
    int StartLauncher(string launcherPath);

    /// <summary>Stops a process and its children (rollback of a started version).</summary>
    void StopTree(int processId);
}

/// <summary>Records the installed version for Windows' "Installed apps" (installed copies only; registry in the Updater).</summary>
public interface IInstalledVersionRecorder
{
    void Record(string layoutRoot, string version);
}

public enum SwapOutcome
{
    /// <summary>The new version started and acknowledged its health; the swap is permanent.</summary>
    Committed,

    /// <summary>The new version was installed but failed; the old version was restored and started again.</summary>
    RolledBack,

    /// <summary>
    /// Nothing was changed (invalid request, app still running, package mismatch, bad archive, locked file). Once the
    /// app has exited for the update, the old version is started again.
    /// </summary>
    Aborted,
}

/// <param name="Reason">Short code for logs and the failure notice; never a path.</param>
public sealed record SwapResult(SwapOutcome Outcome, string? Reason);

/// <summary>
/// The updater protocol (guide P06-06, ADR 0035): validate → wait for exit → re-verify → extract the new payload next to
/// the old one → repoint → launch → health handshake → commit, or roll back on any failure after the layout changed.
/// The old payload and launcher stay intact until the new version confirms its health.
/// </summary>
public sealed partial class UpdateSwap(
    string updatesFolder,
    string dataFolder,
    IUpdaterProcesses processes,
    IInstalledVersionRecorder versionRecorder,
    ILogger logger,
    TimeSpan? exitTimeout = null,
    TimeSpan? healthTimeout = null)
{
    public static readonly TimeSpan DefaultExitTimeout = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan DefaultHealthTimeout = TimeSpan.FromSeconds(60);

    private static readonly string[] RootFiles = [AppLayout.RootLauncher, "LICENSE.txt", "THIRD_PARTY_NOTICES.md"];
    private static readonly TimeSpan HealthPoll = TimeSpan.FromMilliseconds(250);

    private readonly string _updates = Path.GetFullPath(updatesFolder);
    private readonly TimeSpan _exitTimeout = exitTimeout ?? DefaultExitTimeout;
    private readonly TimeSpan _healthTimeout = healthTimeout ?? DefaultHealthTimeout;
    private string? _snapshot;

    public async Task<SwapResult> RunAsync(UpdateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 1. Validate.
        if (Validate(request) is not { } layout)
        {
            return Abort("invalid-request");
        }

        // 2. The running app (and its launcher) must be gone: never overwrite an executable in use.
        foreach (var pid in new[] { (int?)request.AppProcessId, request.LauncherProcessId })
        {
            if (pid is { } id && !await processes.WaitForExitAsync(id, _exitTimeout, cancellationToken).ConfigureAwait(false))
            {
                return Abort("app-still-running");
            }
        }

        // 3. Re-verify the staged package (time of check vs time of use).
        if (!await MatchesAsync(request, cancellationToken).ConfigureAwait(false))
        {
            return AbortAndRestart(request, layout, "package-mismatch");
        }

        // 3b. Copy the user's data while nothing can change it (ADR 0036); no snapshot, no update.
        try
        {
            _snapshot = PreUpdateSnapshot.Take(dataFolder, layout.Pointer.Current, request.Version, DateTimeOffset.UtcNow);
        }
        catch (IOException ex)
        {
            return AbortAndRestart(request, layout, "snapshot-failed:" + ex.GetType().Name);
        }

        // 4. Extract the new payload next to the old one; nothing in use is touched.
        var newPayload = AppLayout.PayloadFolder(layout.Root, request.Version);
        var partial = newPayload + ".partial";
        var replaced = new List<(string Target, bool Existed)>();
        try
        {
            DeleteDirectory(partial);
            DeleteDirectory(newPayload);
            if (!Extract(request, layout.Root, partial, replaced))
            {
                Cleanup(partial, replaced, layout.Root);
                return AbortAndRestart(request, layout, "bad-archive");
            }

            Directory.Move(partial, newPayload);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Cleanup(partial, replaced, layout.Root);
            DeleteDirectory(newPayload);
            return AbortAndRestart(request, layout, "extract-failed:" + ex.GetType().Name);
        }

        // 5–6. Repoint, launch, wait for the health acknowledgment.
        var ack = Path.Combine(_updates, UpdateHandshake.AckFileName(request.Version));
        int? started = null;
        try
        {
            TryDelete(ack);
            WriteAtomic(Path.Combine(_updates, UpdateHandshake.AwaitingHealthFile), new JsonObject { ["version"] = request.Version }.ToJsonString());
            WriteAtomic(layout.PointerPath, AppLayout.SerializePointer(new LayoutPointer(request.Version, layout.Pointer.Current)));
            started = processes.StartLauncher(Path.Combine(layout.Root, AppLayout.RootLauncher));

            if (await WaitForFileAsync(ack, _healthTimeout, cancellationToken).ConfigureAwait(false))
            {
                Commit(request, layout, replaced);
                LogCommitted(logger, request.Version);
                return new SwapResult(SwapOutcome.Committed, null);
            }

            return Rollback(request, layout, replaced, started, "no-health-ack");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return Rollback(request, layout, replaced, started, "swap-failed:" + ex.GetType().Name);
        }
    }

    private Layout? Validate(UpdateRequest request)
    {
        var package = Path.GetFullPath(request.PackagePath);
        var stagingFolder = Path.Combine(_updates, request.Version) + Path.DirectorySeparatorChar;
        var root = Path.GetFullPath(request.LayoutRoot);
        var pointerPath = Path.Combine(root, AppLayout.PointerFileName);

        if (!package.StartsWith(stagingFolder, StringComparison.OrdinalIgnoreCase) || !File.Exists(package)
            || !File.Exists(pointerPath) || !File.Exists(Path.Combine(root, AppLayout.RootLauncher)))
        {
            return null;
        }

        var pointerText = new FileInfo(pointerPath).Length <= AppLayout.MaxPointerBytes ? File.ReadAllText(pointerPath) : null;
        if (AppLayout.ParsePointer(pointerText) is not { } pointer
            || SemanticVersion.TryParse(request.Version) is not { } next
            || SemanticVersion.TryParse(pointer.Current) is not { } current
            || next <= current
            || !Directory.Exists(AppLayout.PayloadFolder(root, pointer.Current)))
        {
            return null;
        }

        return new Layout(root, pointerPath, pointer, pointerText!);
    }

    private static async Task<bool> MatchesAsync(UpdateRequest request, CancellationToken cancellationToken)
    {
        var stream = new FileStream(request.PackagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using (stream.ConfigureAwait(false))
        {
            if (stream.Length != request.Size)
            {
                return false;
            }

            var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            return Convert.ToHexStringLower(hash) == request.Sha256;
        }
    }

    /// <summary>Extracts <c>app-&lt;version&gt;/…</c> into <paramref name="partial"/> and stages root files as <c>.new</c>.</summary>
    private static bool Extract(UpdateRequest request, string root, string partial, List<(string Target, bool Existed)> replaced)
    {
        var prefix = AppLayout.PayloadPrefix + request.Version + "/";
        long total = 0;
        using var archive = ZipFile.OpenRead(request.PackagePath);
        if (archive.Entries.Count > UpdatePackageValidator.MaxZipEntries)
        {
            return false;
        }

        var stagedRoots = new List<string>();
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (name.StartsWith('/') || name.Contains(':', StringComparison.Ordinal) || name.Split('/').Any(p => p is ".." or "."))
            {
                return false;
            }

            total += entry.Length;
            if (total > UpdatePackageValidator.MaxZipBytes)
            {
                return false;
            }

            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var relative = name[prefix.Length..];
                if (relative.Length == 0)
                {
                    continue;
                }

                var target = Path.GetFullPath(Path.Combine(partial, relative));
                if (!target.StartsWith(Path.GetFullPath(partial) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (name.EndsWith('/'))
                {
                    Directory.CreateDirectory(target);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target, overwrite: false);
                }
            }
            else if (!name.Contains('/', StringComparison.Ordinal) && RootFiles.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                var staged = Path.Combine(root, name + ".new");
                entry.ExtractToFile(staged, overwrite: true);
                stagedRoots.Add(name);
            }
        }

        if (!File.Exists(Path.Combine(partial, AppLayout.AppExecutable)))
        {
            foreach (var name in stagedRoots)
            {
                TryDelete(Path.Combine(root, name + ".new"));
            }

            return false;
        }

        // Swap root files (the launcher has exited); keep .bak copies until the new version is healthy.
        foreach (var name in stagedRoots)
        {
            var target = Path.Combine(root, name);
            var staged = target + ".new";
            if (File.Exists(target))
            {
                File.Replace(staged, target, target + ".bak", ignoreMetadataErrors: true);
                replaced.Add((target, true));
            }
            else
            {
                File.Move(staged, target);
                replaced.Add((target, false));
            }
        }

        return true;
    }

    private void Commit(UpdateRequest request, Layout layout, List<(string Target, bool Existed)> replaced)
    {
        MarkSnapshot(SnapshotState.Committed);
        foreach (var (target, _) in replaced)
        {
            TryDelete(target + ".bak");
        }

        // Keep only the new version and the one it replaced (rollback by the launcher stays possible).
        foreach (var folder in Directory.GetDirectories(layout.Root, AppLayout.PayloadPrefix + "*"))
        {
            var name = Path.GetFileName(folder);
            if (!string.Equals(name, AppLayout.PayloadPrefix + request.Version, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(name, AppLayout.PayloadPrefix + layout.Pointer.Current, StringComparison.OrdinalIgnoreCase))
            {
                DeleteDirectory(folder);
            }
        }

        DeleteDirectory(Path.Combine(_updates, request.Version));
        TryDelete(Path.Combine(_updates, UpdateHandshake.AwaitingHealthFile));
        TryDelete(Path.Combine(_updates, UpdateHandshake.AckFileName(request.Version)));
        TryDelete(Path.Combine(_updates, UpdateRequest.FileName));
        TryDelete(Path.Combine(_updates, UpdateHandshake.LastFailureFile));
        versionRecorder.Record(layout.Root, request.Version);
    }

    private SwapResult Rollback(UpdateRequest request, Layout layout, List<(string Target, bool Existed)> replaced, int? started, string reason)
    {
        if (started is { } pid)
        {
            processes.StopTree(pid);
        }

        // The old version compares its data with the snapshot at its next start (the new one may have upgraded it).
        MarkSnapshot(SnapshotState.RolledBack);

        WriteAtomic(layout.PointerPath, layout.PointerText);
        Cleanup(null, replaced, layout.Root);
        DeleteDirectory(AppLayout.PayloadFolder(layout.Root, request.Version));
        TryDelete(Path.Combine(_updates, UpdateHandshake.AwaitingHealthFile));
        WriteAtomic(Path.Combine(_updates, UpdateHandshake.LastFailureFile), new JsonObject { ["version"] = request.Version, ["reason"] = reason }.ToJsonString());
        RestartOld(layout);

        LogRolledBack(logger, request.Version, reason);
        return new SwapResult(SwapOutcome.RolledBack, reason);
    }

    /// <summary>
    /// The app has already exited for the update, but nothing was changed: start the old version again and leave a note
    /// so it can say the update was not installed. The unused data snapshot is removed.
    /// </summary>
    private SwapResult AbortAndRestart(UpdateRequest request, Layout layout, string reason)
    {
        LogAborted(logger, reason);
        if (_snapshot is not null)
        {
            try
            {
                DeleteDirectory(_snapshot);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Harmless; old snapshots are pruned by the next update.
            }
        }

        try
        {
            WriteAtomic(Path.Combine(_updates, UpdateHandshake.LastFailureFile),
                new JsonObject { ["version"] = request.Version, ["reason"] = reason, ["installed"] = false }.ToJsonString());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Only the notice is lost; restarting matters more.
        }

        TryDelete(Path.Combine(_updates, UpdateRequest.FileName));
        RestartOld(layout);
        return new SwapResult(SwapOutcome.Aborted, reason);
    }

    private void RestartOld(Layout layout)
    {
        try
        {
            processes.StartLauncher(Path.Combine(layout.Root, AppLayout.RootLauncher));
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            LogRestartOldFailed(logger, ex.GetType().Name);
        }
    }

    /// <summary>Undo root-file swaps (restore .bak, remove files that did not exist) and remove a partial payload.</summary>
    private static void Cleanup(string? partial, List<(string Target, bool Existed)> replaced, string root)
    {
        if (partial is not null)
        {
            DeleteDirectory(partial);
        }

        foreach (var name in RootFiles)
        {
            TryDelete(Path.Combine(root, name + ".new"));
        }

        foreach (var (target, existed) in replaced)
        {
            var backup = target + ".bak";
            if (existed && File.Exists(backup))
            {
                File.Copy(backup, target, overwrite: true);
                TryDelete(backup);
            }
            else if (!existed)
            {
                TryDelete(target);
            }
        }
    }

    private SwapResult Abort(string reason)
    {
        LogAborted(logger, reason);
        return new SwapResult(SwapOutcome.Aborted, reason);
    }

    private static async Task<bool> WaitForFileAsync(string path, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(path))
            {
                return true;
            }

            await Task.Delay(HealthPoll, cancellationToken).ConfigureAwait(false);
        }

        return File.Exists(path);
    }

    private static void WriteAtomic(string path, string content)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temp, content);
        File.Move(temp, path, overwrite: true);
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort; leftovers are harmless and cleaned by the next update.
        }
    }

    [LoggerMessage(EventId = 3000, Level = LogLevel.Information, Message = "Update to {Version} committed")]
    private static partial void LogCommitted(ILogger logger, string version);

    [LoggerMessage(EventId = 3001, Level = LogLevel.Warning, Message = "Update to {Version} rolled back ({Reason})")]
    private static partial void LogRolledBack(ILogger logger, string version, string reason);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Warning, Message = "Update aborted without changes ({Reason})")]
    private static partial void LogAborted(ILogger logger, string reason);

    [LoggerMessage(EventId = 3009, Level = LogLevel.Warning, Message = "The pre-update snapshot could not be marked {State} ({ErrorType})")]
    private static partial void LogSnapshotMarkFailed(ILogger logger, SnapshotState state, string errorType);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Error, Message = "The previous version could not be restarted after a failed update ({ErrorType})")]
    private static partial void LogRestartOldFailed(ILogger logger, string errorType);

    private void MarkSnapshot(SnapshotState state)
    {
        try
        {
            if (_snapshot is not null)
            {
                PreUpdateSnapshot.Mark(_snapshot, state);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogSnapshotMarkFailed(logger, state, ex.GetType().Name);
        }
    }

    private sealed record Layout(string Root, string PointerPath, LayoutPointer Pointer, string PointerText);
}
