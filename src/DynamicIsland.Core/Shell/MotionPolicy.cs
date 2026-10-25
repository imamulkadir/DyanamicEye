using DynamicIsland.Core.Settings;

namespace DynamicIsland.Core.Shell;

/// <summary>Whether island transitions may animate right now (D08, D10).</summary>
public interface IMotionGate
{
    bool AnimationsEnabled { get; }
}

/// <summary>
/// Pure rule: <c>Off</c>/<c>Reduced</c> never animate; battery/energy saver never animates (power-aware, D10);
/// <c>Full</c> animates otherwise; <c>System</c> follows the Windows "Animation effects" setting.
/// </summary>
public static class MotionPolicy
{
    public static bool ShouldAnimate(MotionPreference preference, bool windowsAnimationsEnabled, bool energySaverOn) => preference switch
    {
        MotionPreference.Off or MotionPreference.Reduced => false,
        _ when energySaverOn => false,
        MotionPreference.Full => true,
        _ => windowsAnimationsEnabled,
    };
}
