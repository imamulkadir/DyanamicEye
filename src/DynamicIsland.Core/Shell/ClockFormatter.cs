using System.Globalization;
using DynamicIsland.Core.Settings;

namespace DynamicIsland.Core.Shell;

/// <summary>
/// Text and update timing of the notch clock (ADR 0016). Pure: culture and time are passed in, so every format is
/// unit-tested. Texts are short enough for the collapsed notch ("Mon Oct 26 · 2:42:05 PM").
/// </summary>
public static class ClockFormatter
{
    public const string Separator = " · ";

    public static string Format(DateTime localTime, ClockSettings settings, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(culture);

        var time = FormatTime(localTime, settings, culture);
        var date = DatePattern(settings.DateFormat, culture) is { } pattern ? localTime.ToString(pattern, culture) : null;
        return date is null ? time : date + Separator + time;
    }

    /// <summary>Time until the displayed text next changes (the next whole second or minute), plus a small margin.</summary>
    public static TimeSpan DelayUntilNextChange(DateTime localTime, bool showSeconds)
    {
        var margin = TimeSpan.FromMilliseconds(15);
        var unit = showSeconds ? TimeSpan.TicksPerSecond : TimeSpan.TicksPerMinute;
        var remaining = unit - (localTime.Ticks % unit);
        return TimeSpan.FromTicks(remaining) + margin;
    }

    private static string FormatTime(DateTime localTime, ClockSettings settings, CultureInfo culture)
    {
        var format = culture.DateTimeFormat;
        switch (settings.HourFormat)
        {
            case ClockHourFormat.TwentyFourHour:
                return localTime.ToString(settings.ShowSeconds ? "HH:mm:ss" : "HH:mm", culture);
            case ClockHourFormat.TwelveHour:
                // Some regions have no AM/PM names; fall back to the invariant ones so 12-hour times stay unambiguous.
                var designators = string.IsNullOrEmpty(format.AMDesignator) ? CultureInfo.InvariantCulture : culture;
                var clock = localTime.ToString(settings.ShowSeconds ? "h:mm:ss" : "h:mm", culture);
                return clock + " " + localTime.ToString("tt", designators);
            default:
                return localTime.ToString(settings.ShowSeconds ? format.LongTimePattern : format.ShortTimePattern, culture);
        }
    }

    private static string? DatePattern(ClockDateFormat dateFormat, CultureInfo culture) => dateFormat switch
    {
        ClockDateFormat.Weekday => "ddd",
        ClockDateFormat.Date => ShortMonthDay(culture),
        ClockDateFormat.WeekdayAndDate => "ddd " + ShortMonthDay(culture),
        ClockDateFormat.Numeric => culture.DateTimeFormat.ShortDatePattern,
        _ => null,
    };

    /// <summary>The region's month-day order with an abbreviated month: "MMMM d" → "MMM d", "d. MMMM" → "d. MMM".</summary>
    private static string ShortMonthDay(CultureInfo culture) =>
        culture.DateTimeFormat.MonthDayPattern.Replace("MMMM", "MMM", StringComparison.Ordinal);
}
