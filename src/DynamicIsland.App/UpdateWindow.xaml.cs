using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using DynamicIsland.Core.Distribution;
using DynamicIsland.Core.Updates;
using Microsoft.Extensions.Logging;

namespace DynamicIsland.App;

/// <summary>
/// One release's details and the update steps (guide §7, P06-04, ADR 0033): version, release date, download size from
/// GitHub, the notes as plain text and a link to the release page. <b>Download</b> and <b>Install</b> are separate
/// buttons the user must select; nothing starts on its own. Closing the window during a download cancels it.
/// </summary>
public partial class UpdateWindow : Window
{
    private readonly UpdateFlow _flow;
    private readonly UpdatePolicy _policy;
    private readonly ILogger _logger;

    public UpdateWindow(UpdateFlow flow, SemanticVersion installed, UpdatePolicy policy, ILogger<UpdateWindow> logger)
    {
        ArgumentNullException.ThrowIfNull(flow);
        _flow = flow;
        _policy = policy;
        _logger = logger;
        InitializeComponent();

        var release = flow.Release;
        HeadingText.Text = $"Dynamic Island {release.Version} is available";
        SummaryText.Text = UpdateMessages.Summary(release, flow.Package, installed, TimeZoneInfo.Local, CultureInfo.CurrentCulture);
        ShowNotes(ReleaseNotes.Parse(release.Notes));

        _flow.Changed += OnFlowChanged;
        Render();
        Loaded += (_, _) => NotesScroller.Focus(); // Not a button: a stray Enter must never approve anything.
    }

    public UpdateFlow Flow => _flow;

    protected override void OnClosing(CancelEventArgs e)
    {
        _flow.CancelDownload();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _flow.Changed -= OnFlowChanged;
        base.OnClosed(e);
    }

    private void OnFlowChanged(object? sender, EventArgs e) => Dispatcher.InvokeAsync(Render);

    private void Render()
    {
        var state = _flow.State;
        ProgressPanel.Visibility = state.Stage == UpdateStage.Downloading ? Visibility.Visible : Visibility.Collapsed;
        if (state.Stage == UpdateStage.Downloading)
        {
            DownloadProgress.Value = state.Total > 0 ? state.Received * 100.0 / state.Total : 0;
            ProgressText.Text = UpdateMessages.Progress(state.Received, state.Total, CultureInfo.CurrentCulture);
        }

        var size = _flow.Package is { } package ? UpdatePackages.FormatSize(package.SizeBytes, CultureInfo.CurrentCulture) : string.Empty;
        (string? Primary, string Secondary, string Status) view = state.Stage switch
        {
            UpdateStage.Review => ($"_Download ({size})", "_Not now", "Nothing is downloaded until you select Download. Installing asks you again."),
            UpdateStage.ManualOnly => ("_Open release page", "_Close", _flow.Package is null
                ? "This release has no file for your copy of Dynamic Island. Get it from the release page."
                : "Download this version from its release page on GitHub."),
            UpdateStage.Managed => (null, "_Close", _policy.Source == UpdatePolicySource.InstallerFlag
                ? "Updates are managed by whoever installed this copy. Ask them for this version."
                : "Updates are managed by your organization. Contact your IT support for this version."),
            UpdateStage.Downloading => (null, "_Cancel download", "Downloading…"),
            UpdateStage.ReadyToInstall => ("_Install now", "_Later", "Downloaded. Installing closes Dynamic Island, updates it and starts it again; your settings and notes are kept."),
            UpdateStage.Installing => (null, "_Close", "Installing…"),
            _ => ("_Retry", "_Close", state.Problem ?? "Something went wrong."),
        };

        PrimaryButton.Visibility = view.Primary is null ? Visibility.Collapsed : Visibility.Visible;
        PrimaryButton.Content = view.Primary;
        SecondaryButton.Content = view.Secondary;
        SecondaryButton.IsEnabled = state.Stage != UpdateStage.Installing;
        StatusText.Text = view.Status;
    }

    private async void OnPrimary(object sender, RoutedEventArgs e)
    {
        switch (_flow.State.Stage)
        {
            case UpdateStage.Review:
                await _flow.ApproveDownloadAsync();
                break;
            case UpdateStage.ReadyToInstall:
                await _flow.ApproveInstallAsync();
                break;
            case UpdateStage.Failed:
                await _flow.RetryAsync();
                break;
            case UpdateStage.ManualOnly:
                OnReleaseLink(sender, e);
                break;
        }
    }

    private void OnSecondary(object sender, RoutedEventArgs e)
    {
        if (_flow.State.Stage == UpdateStage.Downloading)
        {
            _flow.CancelDownload();
            return;
        }

        Close();
    }

    private void OnReleaseLink(object sender, RoutedEventArgs e)
    {
        if (ShellLauncher.TryOpenLink(_flow.Release.PageUrl) is { } errorType)
        {
            AppLog.OpenFolderFailed(_logger, errorType);
            StatusText.Text = $"The browser could not be opened. The release page is {_flow.Release.PageUrl}";
        }
    }

    /// <summary>Plain text only (the notes are untrusted, ADR 0032): headings and bulleted lines, nothing clickable.</summary>
    private void ShowNotes(IReadOnlyList<ReleaseNoteSection> sections)
    {
        if (sections.Count == 0)
        {
            NotesPanel.Children.Add(new TextBlock { Text = "This release has no notes.", Style = (Style)Resources["Hint"] });
            return;
        }

        foreach (var section in sections)
        {
            if (section.Heading.Length > 0)
            {
                NotesPanel.Children.Add(new TextBlock { Text = section.Heading, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 2), TextWrapping = TextWrapping.Wrap });
            }

            foreach (var item in section.Items)
            {
                NotesPanel.Children.Add(new TextBlock { Text = "• " + item, Margin = new Thickness(8, 1, 0, 1), TextWrapping = TextWrapping.Wrap });
            }
        }
    }
}
