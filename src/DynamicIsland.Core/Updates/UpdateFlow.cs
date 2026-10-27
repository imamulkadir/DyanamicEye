using DynamicIsland.Core.Distribution;

namespace DynamicIsland.Core.Updates;

/// <summary>A release file downloaded (and, from P06-05, verified) into the staging folder.</summary>
public sealed record StagedPackage(ReleaseAsset Asset, string Path);

/// <param name="Package">The staged file, on success.</param>
/// <param name="Problem">A short plain sentence for the user, on failure.</param>
public sealed record UpdateDownloadResult(StagedPackage? Package, string? Problem);

/// <summary>Downloads and verifies one release file (guide P06-05, ADR 0034). Reports received bytes; never runs anything.</summary>
public interface IUpdateDownloader
{
    /// <param name="asset">The file of <paramref name="release"/> to download (the other assets carry its signed manifest).</param>
    /// <exception cref="OperationCanceledException">The user cancelled.</exception>
    Task<UpdateDownloadResult> DownloadAsync(ReleaseInfo release, ReleaseAsset asset, IProgress<long> received, CancellationToken cancellationToken);
}

/// <summary>Hands a verified package to the updater (guide P06-06), which closes and restarts the app.</summary>
public interface IUpdateInstaller
{
    /// <returns><c>null</c> once the updater has taken over; otherwise a short plain sentence for the user.</returns>
    Task<string?> InstallAsync(StagedPackage package, CancellationToken cancellationToken);
}

public enum UpdateStage
{
    /// <summary>Release details are shown; waiting for the user's <b>Download</b> approval.</summary>
    Review,

    /// <summary>No in-app download for this copy (no matching file, or not available in this build): the release page only.</summary>
    ManualOnly,

    /// <summary>Self-update is managed by IT (ADR 0031): details and the support route only.</summary>
    Managed,

    Downloading,

    /// <summary>Downloaded; waiting for the user's separate <b>Install</b> approval (or Later).</summary>
    ReadyToInstall,

    Installing,

    /// <summary>The last step failed; <see cref="UpdateFlowState.Problem"/> says why and Retry repeats that step.</summary>
    Failed,
}

/// <param name="Received">Bytes downloaded so far (<see cref="UpdateStage.Downloading"/>).</param>
/// <param name="Total">The file's size from the release metadata, never guessed (guide §7).</param>
public sealed record UpdateFlowState(UpdateStage Stage, long Received = 0, long Total = 0, string? Problem = null);

/// <summary>
/// The update steps for one release (guide §7, P06-04, ADR 0033): details → <b>Download</b> approval → download and verify
/// → separate <b>Install</b> approval (or Later) → install. Nothing happens without the matching user action; cancel
/// returns to the details, a failure offers Retry of the failed step. IT-managed copies only show details.
/// <see cref="Changed"/> may be raised on a background thread (download progress).
/// </summary>
public sealed class UpdateFlow : IDisposable
{
    private readonly IUpdateDownloader? _downloader;
    private readonly IUpdateInstaller? _installer;
    private readonly Lock _lock = new();
    private CancellationTokenSource? _download;
    private StagedPackage? _staged;
    private bool _failedInstalling;

    /// <param name="downloader">In-app download; <c>null</c> where it is not available (the release page only).</param>
    public UpdateFlow(ReleaseInfo release, UpdatePolicy policy, IUpdateDownloader? downloader, IUpdateInstaller? installer)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(policy);
        Release = release;
        Package = UpdatePackages.For(release);
        _downloader = downloader;
        _installer = installer;
        State = new UpdateFlowState(
            policy.SelfUpdateDisabled ? UpdateStage.Managed
            : Package is null || downloader is null || installer is null ? UpdateStage.ManualOnly
            : UpdateStage.Review,
            Total: Package?.SizeBytes ?? 0);
    }

    public event EventHandler? Changed;

    public ReleaseInfo Release { get; }

    /// <summary>The file this copy would download, when the release has one.</summary>
    public ReleaseAsset? Package { get; }

    public UpdateFlowState State { get; private set; }

    /// <summary>The user approved the download (or selected Retry after a failed one).</summary>
    public async Task ApproveDownloadAsync()
    {
        CancellationTokenSource cancel;
        lock (_lock)
        {
            if (State.Stage is not (UpdateStage.Review or UpdateStage.Failed) || (State.Stage == UpdateStage.Failed && _failedInstalling))
            {
                return;
            }

            cancel = _download = new CancellationTokenSource();
            Set(new UpdateFlowState(UpdateStage.Downloading, 0, Package!.SizeBytes));
        }

        var progress = new SynchronousProgress(received =>
        {
            lock (_lock)
            {
                if (State.Stage == UpdateStage.Downloading && ReferenceEquals(_download, cancel))
                {
                    Set(State with { Received = Math.Clamp(received, 0, State.Total) });
                }
            }
        });

        UpdateDownloadResult result;
        try
        {
            result = await _downloader!.DownloadAsync(Release, Package!, progress, cancel.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            result = new UpdateDownloadResult(null, null);
        }
        finally
        {
            lock (_lock)
            {
                _download = null;
            }

            cancel.Dispose();
        }

        lock (_lock)
        {
            if (result.Package is { } package)
            {
                _staged = package;
                Set(new UpdateFlowState(UpdateStage.ReadyToInstall, Package!.SizeBytes, Package.SizeBytes));
            }
            else if (result.Problem is { } problem && !cancel.IsCancellationRequested)
            {
                _failedInstalling = false;
                Set(new UpdateFlowState(UpdateStage.Failed, Total: Package!.SizeBytes, Problem: problem));
            }
            else
            {
                Set(new UpdateFlowState(UpdateStage.Review, Total: Package!.SizeBytes)); // Cancelled: back to the details.
            }
        }
    }

    /// <summary>Cancels a running download; the flow returns to the details.</summary>
    public void CancelDownload()
    {
        lock (_lock)
        {
            _download?.Cancel();
        }
    }

    /// <summary>The user's separate approval to install the downloaded package.</summary>
    public async Task ApproveInstallAsync()
    {
        StagedPackage package;
        lock (_lock)
        {
            if (_staged is null || State.Stage is not (UpdateStage.ReadyToInstall or UpdateStage.Failed) || (State.Stage == UpdateStage.Failed && !_failedInstalling))
            {
                return;
            }

            package = _staged;
            Set(State with { Stage = UpdateStage.Installing, Problem = null });
        }

        var problem = await _installer!.InstallAsync(package, CancellationToken.None).ConfigureAwait(false);
        if (problem is not null)
        {
            lock (_lock)
            {
                _failedInstalling = true;
                Set(State with { Stage = UpdateStage.Failed, Problem = problem });
            }
        }
    }

    /// <summary>Cancels a running download (its own task disposes the token source).</summary>
    public void Dispose() => CancelDownload();

    /// <summary>Retry repeats the step that failed, as the user's renewed approval of it.</summary>
    public Task RetryAsync()
    {
        bool installing;
        lock (_lock)
        {
            if (State.Stage != UpdateStage.Failed)
            {
                return Task.CompletedTask;
            }

            installing = _failedInstalling;
        }

        return installing ? ApproveInstallAsync() : ApproveDownloadAsync();
    }

    private void Set(UpdateFlowState state)
    {
        State = state;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Reports on the downloader's thread (not <see cref="Progress{T}"/>, which posts to a captured context).</summary>
    private sealed class SynchronousProgress(Action<long> report) : IProgress<long>
    {
        public void Report(long value) => report(value);
    }
}
