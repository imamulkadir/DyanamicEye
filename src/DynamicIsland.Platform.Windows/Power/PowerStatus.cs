namespace DynamicIsland.Platform.Windows.Power;

/// <summary>Battery saver / energy saver state from <c>GetSystemPowerStatus</c> (no polling: callers re-read on power events).</summary>
public static class PowerStatus
{
    private const byte SystemStatusFlagSaverOn = 1;

    /// <summary><c>true</c> while Windows battery saver (energy saver on Windows 11) is on; <c>false</c> if unknown.</summary>
    public static bool IsEnergySaverOn() =>
        NativeMethods.GetSystemPowerStatus(out var status) && status.SystemStatusFlag == SystemStatusFlagSaverOn;
}
