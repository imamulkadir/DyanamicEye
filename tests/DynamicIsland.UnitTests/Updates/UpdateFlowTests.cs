using System.Globalization;
using DynamicIsland.Core.Distribution;
using DynamicIsland.Core.Updates;

namespace DynamicIsland.UnitTests.Updates;

/// <summary>The update steps and their separate approvals (P06-04, ADR 0033), with scripted downloader and installer.</summary>
public sealed class UpdateFlowTests
{
    private static readonly ReleaseAsset Setup = new(UpdatePackages.SetupAsset, 51_000_000, new Uri("https://github.com/x/y/releases/download/v1.3.0/" + UpdatePackages.SetupAsset));
    private static readonly ReleaseAsset Zip = new(UpdatePackages.PortableAsset, 72_000_000, new Uri("https://github.com/x/y/releases/download/v1.3.0/" + UpdatePackages.PortableAsset));

    private readonly ScriptedDownloader _downloader = new();
    private readonly ScriptedInstaller _installer = new();

    [Fact]
    public void Nothing_starts_without_an_approval()
    {
        using var flow = Create();

        Assert.Equal(UpdateStage.Review, flow.State.Stage);
        Assert.Equal(51_000_000, flow.State.Total); // From GitHub's metadata, never guessed.
        Assert.Equal(0, _downloader.Downloads);
        Assert.Equal(0, _installer.Installs);
    }

    [Fact]
    public async Task Download_approval_does_not_install_and_install_needs_its_own_approval()
    {
        using var flow = Create();
        _downloader.Complete();

        await flow.ApproveDownloadAsync();

        Assert.Equal(UpdateStage.ReadyToInstall, flow.State.Stage);
        Assert.Equal(1, _downloader.Downloads);
        Assert.Equal(0, _installer.Installs);

        await flow.ApproveInstallAsync();
        Assert.Equal(UpdateStage.Installing, flow.State.Stage);
        Assert.Equal(Setup, _installer.Installed!.Asset);
    }

    [Fact]
    public async Task Install_is_ignored_before_a_download()
    {
        using var flow = Create();

        await flow.ApproveInstallAsync();

        Assert.Equal(UpdateStage.Review, flow.State.Stage);
        Assert.Equal(0, _installer.Installs);
    }

    [Fact]
    public async Task Progress_is_reported_and_cancel_returns_to_the_details()
    {
        using var flow = Create();
        var stages = new List<UpdateFlowState>();
        flow.Changed += (_, _) => stages.Add(flow.State);

        var running = flow.ApproveDownloadAsync();
        _downloader.Report(12_750_000);
        Assert.Equal((UpdateStage.Downloading, 12_750_000L), (flow.State.Stage, flow.State.Received));

        flow.CancelDownload();
        await running;

        Assert.Equal(UpdateStage.Review, flow.State.Stage);
        Assert.Contains(stages, s => s.Stage == UpdateStage.Downloading && s.Received == 12_750_000);
        Assert.Equal(0, _installer.Installs);
    }

    [Fact]
    public async Task A_failed_download_offers_retry_which_downloads_again()
    {
        using var flow = Create();
        _downloader.Fail("The download was interrupted.");

        await flow.ApproveDownloadAsync();
        Assert.Equal((UpdateStage.Failed, "The download was interrupted."), (flow.State.Stage, flow.State.Problem));

        _downloader.Complete();
        await flow.RetryAsync();

        Assert.Equal(UpdateStage.ReadyToInstall, flow.State.Stage);
        Assert.Equal(2, _downloader.Downloads);
        Assert.Equal(0, _installer.Installs);
    }

    [Fact]
    public async Task A_failed_install_retries_the_install_not_the_download()
    {
        using var flow = Create();
        _downloader.Complete();
        await flow.ApproveDownloadAsync();
        _installer.Problem = "The update could not start.";

        await flow.ApproveInstallAsync();
        Assert.Equal((UpdateStage.Failed, "The update could not start."), (flow.State.Stage, flow.State.Problem));

        _installer.Problem = null;
        await flow.RetryAsync();

        Assert.Equal(UpdateStage.Installing, flow.State.Stage);
        Assert.Equal((1, 2), (_downloader.Downloads, _installer.Installs));
    }

    [Fact]
    public void Each_copy_gets_its_own_file()
    {
        using var installed = Create(InstallKind.Installed);
        using var portable = Create(InstallKind.Portable);

        Assert.Equal(Setup, installed.Package);
        Assert.Equal(Zip, portable.Package);
        Assert.Equal(InstallKind.Installed, UpdatePackages.Detect(@"C:\Apps\DI", p => p == Path.Combine(@"C:\Apps\DI", UpdatePackages.UninstallerFileName)));
        Assert.Equal(InstallKind.Portable, UpdatePackages.Detect(@"C:\Apps\DI", _ => false));
    }

    [Fact]
    public async Task Without_a_file_or_in_app_download_only_the_release_page_is_offered()
    {
        using var noFile = new UpdateFlow(Release(Zip), InstallKind.Installed, UpdatePolicy.Allowed, _downloader, _installer);
        using var noDownloader = new UpdateFlow(Release(Setup, Zip), InstallKind.Installed, UpdatePolicy.Allowed, null, null);

        await noFile.ApproveDownloadAsync();
        await noDownloader.ApproveDownloadAsync();

        Assert.Equal(UpdateStage.ManualOnly, noFile.State.Stage);
        Assert.Null(noFile.Package);
        Assert.Equal(UpdateStage.ManualOnly, noDownloader.State.Stage);
        Assert.Equal(0, _downloader.Downloads);
    }

    [Fact]
    public async Task An_it_managed_copy_only_shows_the_details()
    {
        using var flow = Create(policy: new UpdatePolicy(true, UpdatePolicySource.MachinePolicy));

        await flow.ApproveDownloadAsync();

        Assert.Equal(UpdateStage.Managed, flow.State.Stage);
        Assert.Equal(0, _downloader.Downloads);
    }

    [Theory]
    [InlineData(0, "0 bytes")]
    [InlineData(1023, "1023 bytes")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(51_000_000, "48.6 MB")]
    [InlineData(1_288_490_189, "1.2 GB")]
    public void Sizes_read_like_file_explorer(long bytes, string expected) =>
        Assert.Equal(expected, UpdatePackages.FormatSize(bytes, CultureInfo.GetCultureInfo("en-US")));

    [Fact]
    public void Summary_and_progress_texts()
    {
        var culture = CultureInfo.GetCultureInfo("en-US");
        var release = Release(Setup);

        Assert.Equal("You have version 1.2.0. Released Monday, November 2, 2026. Download size: 48.6 MB.",
            UpdateMessages.Summary(release, Setup, new SemanticVersion(1, 2, 0, null), TimeZoneInfo.Utc, culture));
        Assert.Equal("12.2 MB of 48.6 MB (25%)", UpdateMessages.Progress(12_750_000, 51_000_000, culture));
    }

    private UpdateFlow Create(InstallKind kind = InstallKind.Installed, UpdatePolicy? policy = null) =>
        new(Release(Setup, Zip), kind, policy ?? UpdatePolicy.Allowed, _downloader, _installer);

    private static ReleaseInfo Release(params ReleaseAsset[] assets) => new(
        new SemanticVersion(1, 3, 0, null), "v1.3.0", "Dynamic Island 1.3.0", new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.Zero),
        new Uri("https://github.com/x/y/releases/tag/v1.3.0"), "## Features\n- One", assets);

    /// <summary>Each download waits until the test completes, fails or cancels it.</summary>
    private sealed class ScriptedDownloader : IUpdateDownloader
    {
        private TaskCompletionSource<UpdateDownloadResult> _next = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private IProgress<long>? _progress;

        public int Downloads { get; private set; }

        public async Task<UpdateDownloadResult> DownloadAsync(ReleaseInfo release, ReleaseAsset asset, IProgress<long> received, CancellationToken cancellationToken)
        {
            Downloads++;
            _progress = received;
            var next = _next;
            using var registration = cancellationToken.Register(() => next.TrySetCanceled(cancellationToken));
            var result = await next.Task;
            _next = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return result.Package is { } ? result with { Package = new StagedPackage(asset, @"C:\staging\" + asset.Name) } : result;
        }

        public void Report(long received) => _progress!.Report(received);

        public void Complete() => _next.TrySetResult(new UpdateDownloadResult(new StagedPackage(Setup, string.Empty), null));

        public void Fail(string problem) => _next.TrySetResult(new UpdateDownloadResult(null, problem));
    }

    private sealed class ScriptedInstaller : IUpdateInstaller
    {
        public string? Problem { get; set; }

        public int Installs { get; private set; }

        public StagedPackage? Installed { get; private set; }

        public Task<string?> InstallAsync(StagedPackage package, CancellationToken cancellationToken)
        {
            Installs++;
            Installed = package;
            return Task.FromResult(Problem);
        }
    }
}
