using DynamicIsland.Core.Settings;
using DynamicIsland.Core.Shell;

namespace DynamicIsland.UnitTests.Shell;

public sealed class MotionPolicyTests
{
    [Theory]
    // preference,            windows animations, energy saver, expected
    [InlineData(MotionPreference.System, true, false, true)]
    [InlineData(MotionPreference.System, false, false, false)]
    [InlineData(MotionPreference.System, true, true, false)]
    [InlineData(MotionPreference.Full, false, false, true)]
    [InlineData(MotionPreference.Full, true, true, false)]
    [InlineData(MotionPreference.Reduced, true, false, false)]
    [InlineData(MotionPreference.Off, true, false, false)]
    public void Animation_decision(MotionPreference preference, bool windowsAnimations, bool energySaver, bool expected)
    {
        Assert.Equal(expected, MotionPolicy.ShouldAnimate(preference, windowsAnimations, energySaver));
    }
}
