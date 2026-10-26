using DynamicIsland.Core.Audio;
using DynamicIsland.Core.Components;
using DynamicIsland.Core.Shell;
using DynamicIsland.Widgets;

namespace DynamicIsland.UnitTests.Widgets;

/// <summary>The volume widget (P04-05, ADR 0022) against a scripted audio service.</summary>
public sealed class VolumeWidgetTests : IDisposable
{
    private readonly FakeAudio _audio = new();
    private readonly VolumeWidget _widget;
    private readonly Context _context = new();
    private readonly CancellationTokenSource _stop = new();

    public VolumeWidgetTests() => _widget = new VolumeWidget(_audio);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
        _widget.Dispose();
    }

    [Fact]
    public async Task Shows_volume_and_mute_and_follows_external_changes()
    {
        _audio.State = new AudioState(true, 0.42f, false);
        _ = Start(); // Ends with cancellation in Dispose.

        await WaitForAsync(s => s == new VolumeSnapshot(true, 42, false));

        _audio.State = new AudioState(true, 0.8f, true); // E.g. the keyboard volume keys.
        _audio.RaiseChanged();
        await WaitForAsync(s => s == new VolumeSnapshot(true, 80, true));
        Assert.Equal("healthy", _context.Last);
    }

    [Fact]
    public async Task No_output_device_is_shown_and_reported()
    {
        _audio.State = AudioState.NoDevice;
        _ = Start(); // Ends with cancellation in Dispose.

        await WaitForAsync(s => s is { HasDevice: false });

        Assert.Equal("no-output-device", _context.Last);
        Assert.False(await _widget.ExecuteAsync(VolumeWidget.MuteToggleCommand, Ct));
    }

    [Fact]
    public async Task Without_windows_audio_it_idles()
    {
        _audio.Available = false;
        var run = Start();

        await WaitUntilAsync(() => _context.Last == "audio-unavailable");

        Assert.False(run.IsCompleted);
        Assert.Null(_widget.Snapshot);
    }

    [Theory]
    [InlineData("set:0", 0f)]
    [InlineData("set:55", 0.55f)]
    [InlineData("set:100", 1f)]
    public async Task Set_command_sets_the_master_volume(string command, float expected)
    {
        Assert.True(await _widget.ExecuteAsync(command, Ct));
        Assert.Equal(expected, _audio.LastVolume!.Value, 3);
    }

    [Theory]
    [InlineData("set:101")]
    [InlineData("set:-1")]
    [InlineData("set:abc")]
    [InlineData("set:")]
    [InlineData("louder")]
    public async Task Invalid_commands_are_refused(string command)
    {
        Assert.False(await _widget.ExecuteAsync(command, Ct));
        Assert.Null(_audio.LastVolume);
    }

    [Fact]
    public async Task Mute_toggle_flips_the_current_state()
    {
        _audio.State = new AudioState(true, 0.5f, true);

        Assert.True(await _widget.ExecuteAsync(VolumeWidget.MuteToggleCommand, Ct));

        Assert.False(_audio.LastMuted);
        Assert.Equal("set:7", VolumeWidget.SetCommand(7));
        Assert.Equal("set:100", VolumeWidget.SetCommand(140));
    }

    [Theory]
    [InlineData(120, 180)]
    [InlineData(250.2, 251)]
    [InlineData(900, 360)]
    [InlineData(double.NaN, 180)]
    public void Expanded_height_follows_content_within_bounds(double content, double expected)
    {
        Assert.Equal(expected, IslandLayout.ExpandedHeightFor(content));
        Assert.Equal((420, 360), IslandLayout.Max);
    }

    private Task Start() => Task.Run(() => _widget.RunAsync(_context, _stop.Token), Ct);

    private async Task WaitForAsync(Func<VolumeSnapshot?, bool> condition) =>
        await WaitUntilAsync(() => condition(_widget.Snapshot as VolumeSnapshot));

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the condition.");
            await Task.Delay(10, Ct);
        }
    }

    private sealed class Context : IComponentContext
    {
        public string? Last { get; private set; }

        public void ReportHealthy() => Last = "healthy";

        public void ReportDegraded(string reasonCode) => Last = reasonCode;
    }

    private sealed class FakeAudio : IAudioService
    {
        public event EventHandler? Changed;

        public bool Available { get; set; } = true;

        public AudioState State { get; set; } = new(true, 0.5f, false);

        public float? LastVolume { get; private set; }

        public bool? LastMuted { get; private set; }

        public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

        public bool Start() => Available;

        public AudioState GetState() => State;

        public bool SetVolume(float volume)
        {
            LastVolume = volume;
            return true;
        }

        public bool SetMuted(bool muted)
        {
            LastMuted = muted;
            return true;
        }

        public void Dispose()
        {
        }
    }
}
