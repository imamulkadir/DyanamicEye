using System.Globalization;
using DynamicIsland.Core.Audio;
using DynamicIsland.Core.Components;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.Widgets;

namespace DynamicIsland.Widgets;

/// <param name="Percent">Master volume 0–100 of the default output device.</param>
public sealed record VolumeSnapshot(bool HasDevice, int Percent, bool Muted);

/// <summary>
/// Volume and mute of the default output device (guide P04-05, ADR 0022). Event-driven: it re-reads the state only
/// when Windows reports a change (from this app, the keyboard volume keys, Windows or another app) or the default
/// device changes. Commands: <see cref="MuteToggleCommand"/> and <c>set:&lt;0–100&gt;</c>.
/// </summary>
public sealed class VolumeWidget(IAudioService audio) : IWidget, IDisposable
{
    public const string MuteToggleCommand = "mute-toggle";
    public const string SetCommandPrefix = "set:";

    private readonly Lock _gate = new();
    private CancellationTokenSource _wake = new();
    private VolumeSnapshot? _snapshot;

    public event EventHandler? SnapshotChanged;

    public string Id => WidgetIds.Volume;

    public string Name => WidgetRegistry.ComponentName(WidgetIds.Volume);

    public object? Snapshot => Volatile.Read(ref _snapshot);

    public static string SetCommand(int percent) => SetCommandPrefix + Math.Clamp(percent, 0, 100).ToString(CultureInfo.InvariantCulture);

    public async Task RunAsync(IComponentContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!audio.Start())
        {
            context.ReportDegraded("audio-unavailable");
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return;
        }

        audio.Changed += OnAudioChanged;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                CancellationToken wake;
                lock (_gate)
                {
                    wake = _wake.Token;
                }

                var state = audio.GetState();
                Publish(state);
                if (state.HasDevice)
                {
                    context.ReportHealthy();
                }
                else
                {
                    context.ReportDegraded("no-output-device");
                }

                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, wake);
                try
                {
                    await Task.Delay(Timeout.Infinite, linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Woken by a change.
                }
            }
        }
        finally
        {
            audio.Changed -= OnAudioChanged;
        }
    }

    public Task<bool> ExecuteAsync(string command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command == MuteToggleCommand)
        {
            var state = audio.GetState();
            return Task.FromResult(state.HasDevice && audio.SetMuted(!state.Muted));
        }

        if (command.StartsWith(SetCommandPrefix, StringComparison.Ordinal)
            && int.TryParse(command.AsSpan(SetCommandPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var percent)
            && percent is >= 0 and <= 100)
        {
            return Task.FromResult(audio.SetVolume(percent / 100f));
        }

        return Task.FromResult(false);
    }

    public void OnSystemChanged() => Wake();

    public void Release()
    {
        Volatile.Write(ref _snapshot, null);
        SnapshotChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _wake.Dispose();
        }

        audio.Dispose();
    }

    private void Publish(AudioState state)
    {
        var next = new VolumeSnapshot(state.HasDevice, (int)Math.Round(state.Volume * 100), state.Muted);
        if (!Equals(next, Volatile.Read(ref _snapshot)))
        {
            Volatile.Write(ref _snapshot, next);
            SnapshotChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnAudioChanged(object? sender, EventArgs e) => Wake();

    private void Wake()
    {
        CancellationTokenSource previous;
        lock (_gate)
        {
            previous = _wake;
            _wake = new CancellationTokenSource();
        }

        // Not disposed here: the run loop may be linking to its token right now.
        previous.Cancel();
    }
}
