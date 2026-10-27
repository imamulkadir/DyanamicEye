using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DynamicIsland.App;
using DynamicIsland.Core.Distribution;
using DynamicIsland.Core.Updates;
using DynamicIsland.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace DynamicIsland.IntegrationTests;

/// <summary>The real update window (P06-04, ADR 0033) on an offscreen STA window, with scripted download and install.</summary>
public sealed class UpdateWindowTests : IDisposable
{
    private static readonly ReleaseAsset Setup = new(UpdatePackages.SetupAsset, 51_000_000,
        new Uri("https://github.com/x/y/releases/download/v1.3.0/" + UpdatePackages.SetupAsset));

    private static readonly ReleaseInfo Release = new(
        new SemanticVersion(1, 3, 0, null), "v1.3.0", "Dynamic Island 1.3.0", new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.Zero),
        new Uri("https://github.com/x/y/releases/tag/v1.3.0"), "## Features\n- **Media** widget\n- <script>alert(1)</script> stays text\n## Fixes\n- Scroll bar", [Setup]);

    private readonly StaDispatcherThread _ui = new();
    private readonly GatedDownloader _downloader = new();
    private readonly CountingInstaller _installer = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ui.Dispose();

    [Fact]
    public void Details_show_version_date_size_and_plain_notes_and_nothing_starts_on_its_own()
    {
        var window = _ui.Invoke(() => Open(Flow()));

        var (heading, summary, notes, primary, secondary) = _ui.Invoke(() => (
            Text(window, "HeadingText"),
            Text(window, "SummaryText"),
            ((StackPanel)window.FindName("NotesPanel")).Children.OfType<TextBlock>().Select(t => t.Text).ToArray(),
            (string)Button(window, "PrimaryButton").Content,
            (string)Button(window, "SecondaryButton").Content));

        Assert.Equal("Dynamic Island 1.3.0 is available", heading);
        Assert.Contains("You have version 1.2.0", summary, StringComparison.Ordinal);
        Assert.Contains("48.6 MB", summary, StringComparison.Ordinal);
        Assert.Equal(["Features", "• Media widget", "• <script>alert(1)</script> stays text", "Fixes", "• Scroll bar"], notes);
        Assert.Equal("_Download (48.6 MB)", primary);
        Assert.Equal("_Not now", secondary);
        Assert.Equal((0, 0), (_downloader.Downloads, _installer.Installs));
        _ui.Invoke(window.Close);
    }

    [Fact]
    public async Task Download_and_install_are_separate_approvals()
    {
        var window = _ui.Invoke(() => Open(Flow()));

        _ui.Invoke(() => Click(window, "PrimaryButton"));
        await WaitFor(() => _downloader.Downloads == 1);
        _downloader.Report(12_750_000);
        await WaitFor(() => _ui.Invoke(() => Text(window, "ProgressText")).StartsWith("12.2 MB of 48.6 MB", StringComparison.Ordinal));
        Assert.Equal("_Cancel download", _ui.Invoke(() => (string)Button(window, "SecondaryButton").Content));
        Assert.Equal(Visibility.Collapsed, _ui.Invoke(() => Button(window, "PrimaryButton").Visibility));

        _downloader.Complete();
        await WaitFor(() => _ui.Invoke(() => Button(window, "PrimaryButton").Content as string) == "_Install now");
        Assert.Equal(0, _installer.Installs); // Downloaded, not installed.
        Assert.Equal("_Later", _ui.Invoke(() => (string)Button(window, "SecondaryButton").Content));

        _ui.Invoke(() => Click(window, "PrimaryButton"));
        await WaitFor(() => _installer.Installs == 1);
        _ui.Invoke(window.Close);
    }

    [Fact]
    public async Task Cancel_returns_to_the_details_and_closing_cancels_too()
    {
        var window = _ui.Invoke(() => Open(Flow()));
        _ui.Invoke(() => Click(window, "PrimaryButton"));
        await WaitFor(() => _downloader.Downloads == 1);

        _ui.Invoke(() => Click(window, "SecondaryButton"));
        await WaitFor(() => _ui.Invoke(() => Button(window, "PrimaryButton").Content as string) == "_Download (48.6 MB)");
        Assert.True(_ui.Invoke(() => window.IsVisible));

        _ui.Invoke(() => Click(window, "PrimaryButton"));
        await WaitFor(() => _downloader.Downloads == 2);
        _ui.Invoke(window.Close);
        await WaitFor(() => window.Flow.State.Stage == UpdateStage.Review);
        Assert.Equal(0, _installer.Installs);
    }

    [Fact]
    public async Task A_failed_download_says_why_and_retry_tries_again()
    {
        var window = _ui.Invoke(() => Open(Flow()));
        _ui.Invoke(() => Click(window, "PrimaryButton"));
        await WaitFor(() => _downloader.Downloads == 1);

        _downloader.Fail("The download was interrupted. Check your connection and try again.");
        await WaitFor(() => _ui.Invoke(() => Button(window, "PrimaryButton").Content as string) == "_Retry");
        Assert.StartsWith("The download was interrupted", _ui.Invoke(() => Text(window, "StatusText")), StringComparison.Ordinal);

        _ui.Invoke(() => Click(window, "PrimaryButton"));
        await WaitFor(() => _downloader.Downloads == 2);
        _ui.Invoke(window.Close);
    }

    [Theory]
    [InlineData(UpdatePolicySource.MachinePolicy, "your organization")]
    [InlineData(UpdatePolicySource.InstallerFlag, "whoever installed this copy")]
    public void An_it_managed_copy_shows_details_and_the_support_route_without_download(UpdatePolicySource source, string expected)
    {
        var policy = new UpdatePolicy(true, source);
        var window = _ui.Invoke(() => Open(Flow(policy), policy));

        Assert.Equal(Visibility.Collapsed, _ui.Invoke(() => Button(window, "PrimaryButton").Visibility));
        Assert.Contains(expected, _ui.Invoke(() => Text(window, "StatusText")), StringComparison.Ordinal);
        _ui.Invoke(window.Close);
    }

    [Fact]
    public void Without_in_app_download_the_release_page_is_offered()
    {
        var window = _ui.Invoke(() => Open(new UpdateFlow(Release, InstallKind.Installed, UpdatePolicy.Allowed, null, null)));

        Assert.Equal("_Open release page", _ui.Invoke(() => (string)Button(window, "PrimaryButton").Content));
        _ui.Invoke(window.Close);
    }

    [Fact]
    public async Task Reopening_after_later_keeps_the_downloaded_package()
    {
        var service = new UpdateWindowService(_ => Flow(), flow => Open(flow, show: false));
        _ui.Invoke(() => service.Show(Release));
        _ui.Invoke(() => Click(service.Window!, "PrimaryButton"));
        _downloader.Complete();
        await WaitFor(() => service.Window!.Flow.State.Stage == UpdateStage.ReadyToInstall);

        _ui.Invoke(() => Click(service.Window!, "SecondaryButton")); // Later.
        Assert.Null(service.Window);
        _ui.Invoke(() => service.Show(Release));

        Assert.Equal("_Install now", _ui.Invoke(() => (string)Button(service.Window!, "PrimaryButton").Content));
        Assert.Equal(1, _downloader.Downloads);
        _ui.Invoke(service.Window!.Close);
    }

    private UpdateFlow Flow(UpdatePolicy? policy = null) =>
        new(Release, InstallKind.Installed, policy ?? UpdatePolicy.Allowed, _downloader, _installer);

    private static UpdateWindow Open(UpdateFlow flow, UpdatePolicy? policy = null, bool show = true)
    {
        var window = new UpdateWindow(flow, new SemanticVersion(1, 2, 0, null), policy ?? UpdatePolicy.Allowed, NullLogger<UpdateWindow>.Instance)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000,
            Top = -10000,
            ShowActivated = false,
        };
        if (show)
        {
            window.Show();
        }

        return window;
    }

    private static string Text(Window window, string name) => ((TextBlock)window.FindName(name)).Text;

    private static Button Button(Window window, string name) => (Button)window.FindName(name);

    private static void Click(Window window, string name) => Button(window, name).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20, Ct);
        }

        Assert.True(condition());
    }

    private sealed class GatedDownloader : IUpdateDownloader
    {
        private readonly Lock _lock = new();
        private TaskCompletionSource<UpdateDownloadResult> _next = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private IProgress<long>? _progress;
        private int _downloads;

        public int Downloads => Volatile.Read(ref _downloads);

        public async Task<UpdateDownloadResult> DownloadAsync(ReleaseAsset asset, IProgress<long> received, CancellationToken cancellationToken)
        {
            TaskCompletionSource<UpdateDownloadResult> next;
            lock (_lock)
            {
                _progress = received;
                next = _next;
            }

            Interlocked.Increment(ref _downloads);
            using var registration = cancellationToken.Register(() => next.TrySetCanceled(cancellationToken));
            try
            {
                return await next.Task;
            }
            finally
            {
                lock (_lock)
                {
                    _next = new(TaskCreationOptions.RunContinuationsAsynchronously);
                }
            }
        }

        public void Report(long received)
        {
            lock (_lock)
            {
                _progress!.Report(received);
            }
        }

        public void Complete()
        {
            lock (_lock)
            {
                _next.TrySetResult(new UpdateDownloadResult(new StagedPackage(Setup, @"C:\staging\" + Setup.Name), null));
            }
        }

        public void Fail(string problem)
        {
            lock (_lock)
            {
                _next.TrySetResult(new UpdateDownloadResult(null, problem));
            }
        }
    }

    private sealed class CountingInstaller : IUpdateInstaller
    {
        private int _installs;

        public int Installs => Volatile.Read(ref _installs);

        public Task<string?> InstallAsync(StagedPackage package, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _installs);
            return Task.FromResult<string?>(null);
        }
    }
}
