using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using DynamicIsland.Core.Distribution;
using DynamicIsland.Core.Updates;
using DynamicIsland.Infrastructure.Updates;
using DynamicIsland.UnitTests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace DynamicIsland.UnitTests.Updates;

/// <summary>Updater protocol (ADR 0035) on a real temporary release layout with real ZIP packages.</summary>
public sealed class UpdateSwapTests : IDisposable
{
    private const string OldVersion = "1.0.0";
    private const string NewVersion = "1.1.0";
    private const string OldPointer = """{"current":"1.0.0"}""";

    private readonly TempDirectory _temp = new();
    private readonly FakeProcesses _processes = new();
    private readonly RecordingVersion _recorder = new();

    public UpdateSwapTests()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Updates);
        Directory.CreateDirectory(Data);
        File.WriteAllText(Path.Combine(Data, "settings.json"), "settings");
        File.WriteAllText(Path.Combine(Data, "islands.db"), "db");
        File.WriteAllText(Path.Combine(Root, AppLayout.PointerFileName), OldPointer);
        File.WriteAllText(Path.Combine(Root, AppLayout.RootLauncher), "old-launcher");
        File.WriteAllText(Path.Combine(Root, "LICENSE.txt"), "old-license");
        Directory.CreateDirectory(OldPayload);
        File.WriteAllText(Path.Combine(OldPayload, AppLayout.AppExecutable), "old-app");
        _processes.OnStart = _ => File.WriteAllText(Path.Combine(Updates, UpdateHandshake.AckFileName(NewVersion)), "ok");
    }

    private string Root => _temp.Combine("layout");

    private string Updates => _temp.Combine("updates");

    private string Data => _temp.Combine("data");

    private string OldPayload => AppLayout.PayloadFolder(Root, OldVersion);

    private string NewPayload => AppLayout.PayloadFolder(Root, NewVersion);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task Healthy_update_commits_and_keeps_the_previous_version_for_rollback()
    {
        var stale = AppLayout.PayloadFolder(Root, "0.9.0");
        Directory.CreateDirectory(stale);

        var result = await CreateSwap().RunAsync(Request(Package()), Ct);

        Assert.Equal(SwapOutcome.Committed, result.Outcome);
        Assert.Equal(new LayoutPointer(NewVersion, OldVersion), AppLayout.ParsePointer(File.ReadAllText(Path.Combine(Root, AppLayout.PointerFileName))));
        Assert.Equal("new-app", File.ReadAllText(Path.Combine(NewPayload, AppLayout.AppExecutable)));
        Assert.Equal("lib", File.ReadAllText(Path.Combine(NewPayload, "lib", "x.dll")));
        Assert.Equal("old-app", File.ReadAllText(Path.Combine(OldPayload, AppLayout.AppExecutable)));
        Assert.Equal("new-launcher", File.ReadAllText(Path.Combine(Root, AppLayout.RootLauncher)));
        Assert.False(Directory.Exists(stale), "Payloads older than the previous version are removed.");
        Assert.Empty(Directory.GetFiles(Root, "*.bak"));
        Assert.Empty(Directory.GetFiles(Root, "*.new"));
        Assert.False(Directory.Exists(Path.Combine(Updates, NewVersion)), "The staged package is removed after commit.");
        Assert.Equal(NewVersion, _recorder.Version);
        Assert.Single(_processes.Started);
        var snapshot = Assert.Single(Directory.GetDirectories(Data, PreUpdateSnapshot.FolderPrefix + "*"));
        Assert.Equal((OldVersion, NewVersion, SnapshotState.Committed), PreUpdateSnapshot.Read(snapshot));
        Assert.Equal("settings", File.ReadAllText(Path.Combine(snapshot, "settings.json")));
    }

    [Fact]
    public async Task Tidying_up_after_a_healthy_update_never_undoes_it()
    {
        var stale = AppLayout.PayloadFolder(Root, "0.9.0");
        Directory.CreateDirectory(stale);
        _recorder.Failure = new System.Security.SecurityException("registry denied"); // Installed apps entry refused.

        SwapResult result;
        using (new FileStream(Path.Combine(stale, "locked.dll"), FileMode.Create, FileAccess.Write, FileShare.None)) // Old folder in use.
        {
            result = await CreateSwap().RunAsync(Request(Package()), Ct);
        }

        Assert.Equal(SwapOutcome.Committed, result.Outcome);
        Assert.Equal(new LayoutPointer(NewVersion, OldVersion), AppLayout.ParsePointer(File.ReadAllText(Path.Combine(Root, AppLayout.PointerFileName))));
        Assert.True(Directory.Exists(NewPayload));
        Assert.Single(_processes.Started);
        Assert.Empty(_processes.Stopped); // The healthy new version keeps running.
    }

    [Fact]
    public async Task A_rollback_that_cannot_remove_the_new_version_still_restarts_the_old_one()
    {
        FileStream? held = null;
        _processes.OnStart = _ => held = new FileStream(Path.Combine(NewPayload, "lib", "x.dll"), FileMode.Open, FileAccess.Read, FileShare.None); // A dying process.

        SwapResult result;
        try
        {
            result = await CreateSwap(healthTimeout: TimeSpan.FromMilliseconds(300)).RunAsync(Request(Package()), Ct);
        }
        finally
        {
            held?.Dispose();
        }

        Assert.Equal(SwapOutcome.RolledBack, result.Outcome);
        Assert.Equal(OldPointer, File.ReadAllText(Path.Combine(Root, AppLayout.PointerFileName)));
        Assert.Equal("old-launcher", File.ReadAllText(Path.Combine(Root, AppLayout.RootLauncher)));
        Assert.Equal(2, _processes.Started.Count); // The old version was started again.
        Assert.Contains(NewVersion, File.ReadAllText(Path.Combine(Updates, UpdateHandshake.LastFailureFile)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_health_acknowledgment_rolls_back_to_the_untouched_old_version()
    {
        _processes.OnStart = _ => { }; // The new version never confirms its health (crash, hang).

        var result = await CreateSwap(healthTimeout: TimeSpan.FromMilliseconds(300)).RunAsync(Request(Package()), Ct);

        Assert.Equal(SwapOutcome.RolledBack, result.Outcome);
        Assert.Equal("no-health-ack", result.Reason);
        Assert.Equal(OldPointer, File.ReadAllText(Path.Combine(Root, AppLayout.PointerFileName)));
        Assert.Equal("old-launcher", File.ReadAllText(Path.Combine(Root, AppLayout.RootLauncher)));
        Assert.Equal("old-license", File.ReadAllText(Path.Combine(Root, "LICENSE.txt")));
        Assert.Equal("old-app", File.ReadAllText(Path.Combine(OldPayload, AppLayout.AppExecutable)));
        Assert.False(Directory.Exists(NewPayload));
        Assert.Equal([_processes.Started[0]], _processes.Stopped);
        Assert.Equal(2, _processes.Started.Count); // New version, then the restored old version.
        Assert.Contains(NewVersion, File.ReadAllText(Path.Combine(Updates, UpdateHandshake.LastFailureFile)), StringComparison.Ordinal);
        Assert.Null(_recorder.Version);
        Assert.Equal(SnapshotState.RolledBack, PreUpdateSnapshot.Read(Assert.Single(Directory.GetDirectories(Data, PreUpdateSnapshot.FolderPrefix + "*")))?.State);
    }

    [Fact]
    public async Task Data_that_cannot_be_copied_aborts_before_anything_changes()
    {
        using (new FileStream(Path.Combine(Data, "islands.db"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var result = await CreateSwap().RunAsync(Request(Package()), Ct);

            Assert.Equal(SwapOutcome.Aborted, result.Outcome);
            Assert.Equal("snapshot-failed:IOException", result.Reason);
        }

        Assert.Equal(OldPointer, File.ReadAllText(Path.Combine(Root, AppLayout.PointerFileName)));
        Assert.False(Directory.Exists(NewPayload));
        AssertOldRestarted();
        Assert.Empty(Directory.GetDirectories(Data, PreUpdateSnapshot.FolderPrefix + "*"));
    }

    [Fact]
    public async Task App_still_running_aborts_without_changing_anything()
    {
        _processes.ExitsInTime = false;

        var result = await CreateSwap().RunAsync(Request(Package()), Ct);

        Assert.Equal(SwapOutcome.Aborted, result.Outcome);
        Assert.Equal("app-still-running", result.Reason);
        AssertUnchanged();
        Assert.Empty(_processes.Started);
    }

    [Fact]
    public async Task Package_changed_after_download_aborts()
    {
        var package = Package();
        var request = Request(package) with { Sha256 = new string('0', 64) };

        var result = await CreateSwap().RunAsync(request, Ct);

        Assert.Equal("package-mismatch", result.Reason);
        AssertUnchanged();
        AssertOldRestarted();
    }

    [Fact]
    public async Task Archive_escaping_the_layout_aborts_and_leaves_nothing_behind()
    {
        var package = Package(extra: ($"app-{NewVersion}/../../evil.txt", "x"));

        var result = await CreateSwap().RunAsync(Request(package), Ct);

        Assert.Equal("bad-archive", result.Reason);
        Assert.False(File.Exists(_temp.Combine("evil.txt")));
        Assert.False(Directory.Exists(NewPayload + ".partial"));
        AssertUnchanged();
        AssertOldRestarted();
    }

    [Fact]
    public async Task Archive_without_the_app_aborts()
    {
        var package = Package(includeApp: false);

        var result = await CreateSwap().RunAsync(Request(package), Ct);

        Assert.Equal("bad-archive", result.Reason);
        AssertUnchanged();
        AssertOldRestarted();
    }

    [Fact]
    public async Task Locked_launcher_aborts_and_restores_the_layout()
    {
        using var locked = new FileStream(Path.Combine(Root, AppLayout.RootLauncher), FileMode.Open, FileAccess.Read, FileShare.None);

        var result = await CreateSwap().RunAsync(Request(Package()), Ct);

        Assert.Equal(SwapOutcome.Aborted, result.Outcome);
        Assert.StartsWith("extract-failed", result.Reason, StringComparison.Ordinal);
        locked.Dispose();
        AssertUnchanged();
        AssertOldRestarted();
    }

    [Fact]
    public async Task A_package_locked_during_the_recheck_restarts_the_old_version_instead_of_crashing()
    {
        var package = Package();
        var request = Request(package);
        SwapResult result;
        using (new FileStream(package, FileMode.Open, FileAccess.Read, FileShare.None)) // e.g. an antivirus scan
        {
            result = await CreateSwap().RunAsync(request, Ct);
        }

        Assert.Equal(SwapOutcome.Aborted, result.Outcome);
        Assert.Equal("package-unreadable:IOException", result.Reason);
        AssertUnchanged();
        AssertOldRestarted();
    }

    [Fact]
    public async Task Abort_after_the_app_exited_restarts_it_and_says_nothing_was_installed()
    {
        var handover = new UpdateHandover(Updates, NullLogger.Instance);
        File.WriteAllText(Path.Combine(Updates, UpdateRequest.FileName), "{}");
        using (new FileStream(Path.Combine(Root, "LICENSE.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = await CreateSwap().RunAsync(Request(Package()), Ct);

            Assert.Equal(SwapOutcome.Aborted, result.Outcome);
        }

        AssertUnchanged();
        AssertOldRestarted();
        Assert.Empty(Directory.GetDirectories(Data, PreUpdateSnapshot.FolderPrefix + "*")); // Nothing changed, so no snapshot is kept.
        Assert.False(File.Exists(Path.Combine(Updates, UpdateRequest.FileName)));
        Assert.True(File.Exists(Path.Combine(Updates, NewVersion, UpdatePackages.PortableAsset))); // Kept for a retry.
        Assert.Equal(new UpdateFailure(NewVersion, Installed: false), handover.TakeFailure());
    }

    [Theory]
    [InlineData("1.0.0")] // same version
    [InlineData("0.9.0")] // downgrade
    public async Task Same_or_older_versions_are_rejected(string version)
    {
        var result = await CreateSwap().RunAsync(Request(Package(version: version), version), Ct);

        Assert.Equal("invalid-request", result.Reason);
        AssertUnchanged();
    }

    [Fact]
    public async Task Package_outside_the_updates_folder_is_rejected()
    {
        var outside = _temp.Combine("elsewhere.zip");
        File.Copy(Package(), outside);

        var result = await CreateSwap().RunAsync(Request(outside), Ct);

        Assert.Equal("invalid-request", result.Reason);
    }

    [Fact]
    public void Request_round_trips_and_rejects_invalid_content()
    {
        var request = new UpdateRequest("1.2.3", @"C:\u\1.2.3\p.zip", 10, new string('a', 64), @"C:\layout", 42, null);

        Assert.Equal(request, UpdateRequest.Parse(request.Serialize()));
        Assert.Null(UpdateRequest.Parse(request.Serialize().Replace(new string('a', 64), "XYZ", StringComparison.Ordinal)));
        Assert.Null(UpdateRequest.Parse(request.Serialize().Replace("1.2.3\"", "..\\\"", StringComparison.Ordinal)));
        Assert.Null(UpdateRequest.Parse("{}"));
        Assert.Null(UpdateRequest.Parse(new string(' ', UpdateRequest.MaxBytes + 1)));
    }

    [Fact]
    public async Task App_hand_over_drives_the_swap_and_the_new_version_acknowledges_its_health()
    {
        var handover = new UpdateHandover(Updates, NullLogger.Instance);
        _processes.OnStart = _ => handover.AcknowledgeHealth(NewPayload + Path.DirectorySeparatorChar); // The new app's first start.

        Assert.Null(handover.Prepare(Staged(Package()), OldPayload, 1234, 5678));
        var request = UpdateRequest.Parse(File.ReadAllText(handover.RequestPath));
        Assert.NotNull(request);
        Assert.Equal((NewVersion, Root, 1234, 5678), (request.Version, request.LayoutRoot, request.AppProcessId, request.LauncherProcessId));

        var result = await CreateSwap().RunAsync(request, Ct);

        Assert.Equal(SwapOutcome.Committed, result.Outcome);
        Assert.False(File.Exists(handover.RequestPath));
        Assert.Null(handover.TakeFailure());
    }

    [Fact]
    public void Hand_over_is_refused_outside_the_release_layout_and_for_unstaged_packages()
    {
        var handover = new UpdateHandover(Updates, NullLogger.Instance);
        var package = Package();
        var elsewhere = Path.Combine(_temp.Combine("downloads"), UpdatePackages.PortableAsset);
        Directory.CreateDirectory(Path.GetDirectoryName(elsewhere)!);
        File.Copy(package, elsewhere);
        var dev = _temp.Combine("bin");
        Directory.CreateDirectory(dev);

        Assert.Equal("not-release-layout", handover.Prepare(Staged(package), dev, 1, null));
        Assert.Equal("not-staged", handover.Prepare(Staged(elsewhere), OldPayload, 1, null));
        Assert.Equal("not-staged", handover.Prepare(new StagedPackage(Asset(UpdatePackages.SetupAsset), package), OldPayload, 1, null));
        Assert.False(File.Exists(handover.RequestPath));
    }

    [Fact]
    public void Health_is_acknowledged_only_by_the_awaited_version()
    {
        var handover = new UpdateHandover(Updates, NullLogger.Instance);
        var awaiting = Path.Combine(Updates, UpdateHandshake.AwaitingHealthFile);
        var ack = Path.Combine(Updates, UpdateHandshake.AckFileName(NewVersion));

        Assert.False(handover.AcknowledgeHealth(NewPayload)); // Nothing awaited: a normal start.
        File.WriteAllText(awaiting, """{"version":5}""");
        Assert.False(handover.AcknowledgeHealth(NewPayload));
        File.WriteAllText(awaiting, $$"""{"version":"{{NewVersion}}"}""");
        Assert.False(handover.AcknowledgeHealth(OldPayload));
        Assert.False(handover.AcknowledgeHealth(_temp.Combine("bin")));
        Assert.False(File.Exists(ack));

        Assert.True(handover.AcknowledgeHealth(NewPayload));
        Assert.True(File.Exists(ack));
    }

    [Fact]
    public async Task Rollback_note_is_taken_once()
    {
        var handover = new UpdateHandover(Updates, NullLogger.Instance);
        _processes.OnStart = _ => { };
        await CreateSwap(TimeSpan.FromMilliseconds(300)).RunAsync(Request(Package()), Ct);

        Assert.Equal(new UpdateFailure(NewVersion, Installed: true), handover.TakeFailure());
        Assert.Null(handover.TakeFailure());

        File.WriteAllText(Path.Combine(Updates, UpdateHandshake.LastFailureFile), """{"version":"..\\x"}""");
        Assert.Null(handover.TakeFailure());
    }

    private static ReleaseAsset Asset(string name) => new(name, 1, new Uri("https://github.com/x/y/releases/download/v1.1.0/" + name));

    private static StagedPackage Staged(string path) => new(Asset(UpdatePackages.PortableAsset), path);

    private UpdateSwap CreateSwap(TimeSpan? healthTimeout = null) =>
        new(Updates, Data, _processes, _recorder, NullLogger.Instance, TimeSpan.FromSeconds(1), healthTimeout ?? TimeSpan.FromSeconds(5));

    private UpdateRequest Request(string package, string version = NewVersion)
    {
        var bytes = File.ReadAllBytes(package);
        return new UpdateRequest(version, package, bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)), Root, 1234, 5678);
    }

    private string Package(string version = NewVersion, bool includeApp = true, (string Name, string Content)? extra = null)
    {
        var folder = Path.Combine(Updates, version);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, UpdatePackages.PortableAsset);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            void Add(string name, string content)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open(), Encoding.UTF8);
                writer.Write(content);
            }

            Add(AppLayout.RootLauncher, "new-launcher");
            Add("LICENSE.txt", "new-license");
            Add(AppLayout.PointerFileName, $$"""{"current":"{{version}}"}""");
            if (includeApp)
            {
                Add($"app-{version}/{AppLayout.AppExecutable}", "new-app");
            }

            Add($"app-{version}/lib/x.dll", "lib");
            if (extra is { } e)
            {
                Add(e.Name, e.Content);
            }
        }

        return path;
    }

    private void AssertUnchanged()
    {
        Assert.Equal(OldPointer, File.ReadAllText(Path.Combine(Root, AppLayout.PointerFileName)));
        Assert.Equal("old-launcher", File.ReadAllText(Path.Combine(Root, AppLayout.RootLauncher)));
        Assert.Equal("old-license", File.ReadAllText(Path.Combine(Root, "LICENSE.txt")));
        Assert.Equal("old-app", File.ReadAllText(Path.Combine(OldPayload, AppLayout.AppExecutable)));
        Assert.False(Directory.Exists(NewPayload));
        Assert.Empty(Directory.GetFiles(Root, "*.new"));
        Assert.Empty(Directory.GetFiles(Root, "*.bak"));
    }

    /// <summary>The app had already exited for the update, so the untouched old version must be started again.</summary>
    private void AssertOldRestarted() => Assert.Single(_processes.Started);

    private sealed class FakeProcesses : IUpdaterProcesses
    {
        private int _nextPid = 9000;

        public bool ExitsInTime { get; set; } = true;

        public Action<string> OnStart { get; set; } = _ => { };

        public List<int> Started { get; } = [];

        public List<int> Stopped { get; } = [];

        public Task<bool> WaitForExitAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken) => Task.FromResult(ExitsInTime);

        public int StartLauncher(string launcherPath)
        {
            var pid = _nextPid++;
            Started.Add(pid);
            if (Started.Count == 1)
            {
                OnStart(launcherPath);
            }

            return pid;
        }

        public void StopTree(int processId) => Stopped.Add(processId);
    }

    private sealed class RecordingVersion : IInstalledVersionRecorder
    {
        public string? Version { get; private set; }

        public Exception? Failure { get; set; }

        public void Record(string layoutRoot, string version)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            Version = version;
        }
    }
}
