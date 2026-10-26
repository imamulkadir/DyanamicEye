using System.Windows;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.Shell;
using DynamicIsland.Platform.Windows.Power;
using Microsoft.Win32;

namespace DynamicIsland.App;

/// <summary>
/// Live inputs for <see cref="MotionPolicy"/>: the user's motion setting, Windows "Animation effects" and battery/energy
/// saver (cached, refreshed on <see cref="SystemEvents.PowerModeChanged"/> — no polling).
/// </summary>
public sealed class MotionGate : IMotionGate, IDisposable
{
    private readonly ISettingsService _settings;
    private volatile bool _energySaverOn;

    public MotionGate(ISettingsService settings)
    {
        _settings = settings;
        _energySaverOn = PowerStatus.IsEnergySaverOn();
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    public bool AnimationsEnabled =>
        MotionPolicy.ShouldAnimate(_settings.Current.Appearance.Motion, SystemParameters.ClientAreaAnimation, _energySaverOn);

    public void Dispose() => SystemEvents.PowerModeChanged -= OnPowerModeChanged;

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e) => _energySaverOn = PowerStatus.IsEnergySaverOn();
}
