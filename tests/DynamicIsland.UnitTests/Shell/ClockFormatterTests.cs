using System.Globalization;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.Shell;

namespace DynamicIsland.UnitTests.Shell;

public sealed class ClockFormatterTests
{
    private static readonly DateTime Afternoon = new(2026, 10, 26, 14, 42, 5, DateTimeKind.Local);
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    [Theory]
    [InlineData(ClockHourFormat.System, false, "2:42 PM")]
    [InlineData(ClockHourFormat.System, true, "2:42:05 PM")]
    [InlineData(ClockHourFormat.TwelveHour, false, "2:42 PM")]
    [InlineData(ClockHourFormat.TwentyFourHour, false, "14:42")]
    [InlineData(ClockHourFormat.TwentyFourHour, true, "14:42:05")]
    public void Time_follows_the_chosen_hour_format(ClockHourFormat hours, bool seconds, string expected)
    {
        var settings = new ClockSettings { HourFormat = hours, ShowSeconds = seconds };

        Assert.Equal(expected, ClockFormatter.Format(Afternoon, settings, English));
    }

    [Fact]
    public void System_format_follows_the_region()
    {
        Assert.Equal("14:42", ClockFormatter.Format(Afternoon, new ClockSettings(), German));
    }

    [Fact]
    public void Twelve_hour_time_always_has_a_designator()
    {
        var noDesignators = (CultureInfo)CultureInfo.GetCultureInfo("de-DE").Clone();
        noDesignators.DateTimeFormat.AMDesignator = string.Empty;
        noDesignators.DateTimeFormat.PMDesignator = string.Empty;

        Assert.Equal("2:42 PM", ClockFormatter.Format(Afternoon, new ClockSettings { HourFormat = ClockHourFormat.TwelveHour }, noDesignators));
    }

    [Theory]
    [InlineData(ClockDateFormat.None, "2:42 PM")]
    [InlineData(ClockDateFormat.Weekday, "Mon · 2:42 PM")]
    [InlineData(ClockDateFormat.Date, "Oct 26 · 2:42 PM")]
    [InlineData(ClockDateFormat.WeekdayAndDate, "Mon Oct 26 · 2:42 PM")]
    [InlineData(ClockDateFormat.Numeric, "10/26/2026 · 2:42 PM")]
    public void Date_formats_come_before_the_time(ClockDateFormat date, string expected)
    {
        Assert.Equal(expected, ClockFormatter.Format(Afternoon, new ClockSettings { DateFormat = date }, English));
    }

    [Fact]
    public void Date_order_follows_the_region()
    {
        var text = ClockFormatter.Format(Afternoon, new ClockSettings { DateFormat = ClockDateFormat.Date }, German);

        Assert.StartsWith("26. Okt", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, 54_985)]
    [InlineData(true, 1_000)]
    public void Next_update_is_at_the_next_minute_or_second(bool seconds, int expectedMilliseconds)
    {
        var now = Afternoon.AddMilliseconds(seconds ? 0 : 15);

        var delay = ClockFormatter.DelayUntilNextChange(now, seconds);

        Assert.Equal(expectedMilliseconds + 15, delay.TotalMilliseconds, precision: 0);
        Assert.True(delay > TimeSpan.Zero);
    }

    [Fact]
    public void Missing_or_unknown_clock_settings_are_repaired()
    {
        var settings = new AppSettings { Appearance = new AppearanceSettings { Clock = null! } };
        var unknown = new AppSettings { Appearance = new AppearanceSettings { Clock = new ClockSettings { DateFormat = (ClockDateFormat)42 } } };

        Assert.Equal(new ClockSettings(), SettingsValidator.Normalize(settings).Settings.Appearance.Clock);
        Assert.Equal(ClockDateFormat.None, SettingsValidator.Normalize(unknown).Settings.Appearance.Clock.DateFormat);
    }
}
