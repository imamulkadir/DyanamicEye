using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using DynamicIsland.App;
using DynamicIsland.Core.Audio;
using DynamicIsland.Core.Components;
using DynamicIsland.Core.Lifecycle;
using DynamicIsland.Core.Notifications;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.Shell;
using DynamicIsland.Core.Widgets;
using DynamicIsland.Platform.Windows.Audio;
using DynamicIsland.TestSupport;
using DynamicIsland.Widgets;
using Microsoft.Extensions.Logging.Abstractions;

namespace DynamicIsland.IntegrationTests;

/// <summary>Volume widget, persistent views and content-sized expansion (P04-05, ADR 0022).</summary>
public sealed class VolumeAndLayoutTests : IDisposable
{
    private static readonly ComponentStatus Healthy = new("x", ComponentState.Healthy);

    private readonly StaDispatcherThread _ui = new();
    private readonly LifecycleCoordinator _lifecycle = new(TimeProvider.System);

    public void Dispose()
    {
        _ui.Dispose();
        _lifecycle.Dispose();
    }

    [Fact]
    public void Core_audio_connects_and_reads_without_changing_anything()
    {
        using var audio = new CoreAudioService();

        Assert.True(audio.Start(), "Core Audio should be available on Windows.");
        var state = audio.GetState();

        // Writing the same level back proves the call path without changing what the user hears.
        Assert.True(!state.HasDevice || audio.SetVolume(state.Volume));
        Assert.InRange(state.Volume, 0f, 1f);
    }

    [Fact]
    public async Task Slider_sends_commands_and_is_not_overwritten_while_the_user_adjusts()
    {
        var audio = new ScriptedAudio();
        using var host = NewHost();
        using var registry = new WidgetRegistry(host, [new VolumeWidget(audio)]);
        registry.Register([new WidgetSetting(WidgetIds.Volume, true)]);
        host.StartAll();
        await WaitAsync(() => registry.Slots.Single().Snapshot is not null);

        var (slider, elementBefore, elementAfter) = _ui.Invoke(() =>
        {
            var presenter = new WidgetPresenter(registry, new NotchView(), null!, null!, _lifecycle, NullLogger<WidgetPresenter>.Instance);
            var element = presenter.ViewFor(registry.Slots.Single())!;
            var s = FindSlider(element);
            s.Value = 73; // As a drag or arrow key would.

            // An update from Windows with the old value arrives right away: the slider must stay where the user put it.
            var again = presenter.ViewFor(registry.Slots.Single() with { Snapshot = new VolumeSnapshot(true, 30, false) })!;
            return (s, element, again);
        });

        Assert.Same(elementBefore, elementAfter); // Persistent view (ADR 0022).
        Assert.Equal(73, _ui.Invoke(() => slider.Value));
        Assert.True(await WaitAsync(() => audio.LastVolume is { } v && Math.Abs(v - 0.73f) < 0.001f), "The slider value must reach the audio service.");
    }

    [Fact]
    public void No_output_device_disables_the_controls()
    {
        var (sliderVisible, muteEnabled, text) = _ui.Invoke(() =>
        {
            using var host = NewHost();
            using var registry = new WidgetRegistry(host, []);
            var presenter = new WidgetPresenter(registry, new NotchView(), null!, null!, _lifecycle, NullLogger<WidgetPresenter>.Instance);
            var element = presenter.ViewFor(new WidgetSlot(WidgetIds.Volume, "Volume", Healthy, new VolumeSnapshot(false, 0, false)))!;
            var grid = (Grid)element;
            return (FindSlider(element).IsVisible || FindSlider(element).Visibility == Visibility.Visible,
                grid.Children.OfType<System.Windows.Controls.Primitives.ToggleButton>().Single().IsEnabled,
                grid.Children.OfType<TextBlock>().Single(t => t.Visibility == Visibility.Visible && t.Text.Length > 0).Text);
        });

        Assert.False(sliderVisible);
        Assert.False(muteEnabled);
        Assert.Equal("No audio output", text);
    }

    [Fact]
    public void Expanded_height_follows_the_selected_tab()
    {
        var (clockHeight, tasksHeight) = _ui.Invoke(() =>
        {
            using var host = NewHost();
            using var registry = new WidgetRegistry(host, []);
            var view = new NotchView { Width = 420, Height = 360 };
            var presenter = new WidgetPresenter(registry, view, null!, null!, _lifecycle, NullLogger<WidgetPresenter>.Instance);
            view.ApplyVisualState(IslandState.Expanded, 360, 1.0);
            var now = DateTimeOffset.Now;
            WidgetSlot[] slots =
            [
                new(WidgetIds.Clock, "Clock", Healthy, new ClockSnapshot(DateTime.Now, "2:42 PM", "2:42 PM", "Monday, October 26, 2026")),
                new(WidgetIds.Volume, "Volume", Healthy, new VolumeSnapshot(true, 50, false)),
                new(WidgetIds.Tasks, "Tasks", Healthy, new TasksSnapshot(
                    [.. Enumerable.Range(1, 6).Select(i => new Core.PersonalData.TaskItem(i, $"Task {i}", false, now, now, null))], 6, 0, false, null)),
            ];
            view.ShowWidgets(slots, presenter.ViewFor);
            var clock = view.MeasureExpandedHeight(420);
            view.SelectWidget(WidgetIds.Tasks);
            return (clock, view.MeasureExpandedHeight(420));
        });

        Assert.True(tasksHeight > clockHeight + 60, $"Six tasks ({tasksHeight}) must need more room than the clock ({clockHeight}).");
        Assert.InRange(IslandLayout.ExpandedHeightFor(clockHeight), IslandLayout.Expanded.Height, IslandLayout.ExpandedMaxHeight);
    }

    private ComponentHost NewHost() =>
        new(_lifecycle, new InMemoryNotificationService(TimeProvider.System), new DefaultFailureClassifier(), LaunchArguments.Default, TimeProvider.System);

    private static Slider FindSlider(DependencyObject root) =>
        LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>().Select(c => c as Slider ?? (c is Panel ? FindSliderOrNull(c) : null)).First(s => s is not null)!;

    private static Slider? FindSliderOrNull(DependencyObject root) =>
        LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>().Select(c => c as Slider ?? FindSliderOrNull(c)).FirstOrDefault(s => s is not null);

    private static async Task<bool> WaitAsync(Func<bool> condition)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.Elapsed > TimeSpan.FromSeconds(5))
            {
                return false;
            }

            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        return true;
    }

    private sealed class ScriptedAudio : IAudioService
    {
        private readonly Lock _gate = new();
        private float? _lastVolume;

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }

        public float? LastVolume
        {
            get
            {
                lock (_gate)
                {
                    return _lastVolume;
                }
            }
        }

        public bool Start() => true;

        public AudioState GetState() => new(true, 0.3f, false);

        public bool SetVolume(float volume)
        {
            lock (_gate)
            {
                _lastVolume = volume;
            }

            return true;
        }

        public bool SetMuted(bool muted) => true;

        public void Dispose()
        {
        }
    }
}
