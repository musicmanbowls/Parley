using System.Globalization;

namespace Parley.Core;

/// <summary>Timestamps as people read them in a messenger. All input is Unix milliseconds, UTC.</summary>
public static class TimeText
{
    private static readonly CultureInfo English = CultureInfo.InvariantCulture;

    public static DateTime ToLocal(long unixMs, TimeZoneInfo? zone = null) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTimeOffset.FromUnixTimeMilliseconds(unixMs).UtcDateTime, zone ?? TimeZoneInfo.Local);

    public static string Clock(DateTime local, bool use24Hour) =>
        local.ToString(use24Hour ? "HH:mm" : "h:mm tt", English);

    public static string Clock(long unixMs, bool use24Hour, TimeZoneInfo? zone = null) =>
        Clock(ToLocal(unixMs, zone), use24Hour);

    /// <summary>The compact stamp beside a conversation in the sidebar.</summary>
    public static string Sidebar(long unixMs, long nowMs, bool use24Hour, TimeZoneInfo? zone = null)
    {
        if (unixMs <= 0) return string.Empty;
        var when = ToLocal(unixMs, zone);
        var now = ToLocal(nowMs, zone);
        var days = (now.Date - when.Date).Days;

        if (days <= 0) return Clock(when, use24Hour);
        if (days == 1) return "Yesterday";
        if (days < 7) return when.ToString("ddd", English);
        return when.Year == now.Year ? when.ToString("d MMM", English) : when.ToString("d MMM yy", English);
    }

    /// <summary>The centred line drawn in a thread when time has visibly passed.</summary>
    public static string Divider(long unixMs, long nowMs, bool use24Hour, TimeZoneInfo? zone = null)
    {
        var when = ToLocal(unixMs, zone);
        var now = ToLocal(nowMs, zone);
        var days = (now.Date - when.Date).Days;
        var clock = Clock(when, use24Hour);

        if (days <= 0) return $"Today {clock}";
        if (days == 1) return $"Yesterday {clock}";
        if (days < 7) return $"{when.ToString("dddd", English)} {clock}";
        return when.Year == now.Year
            ? $"{when.ToString("ddd d MMM", English)} {clock}"
            : $"{when.ToString("d MMM yyyy", English)} {clock}";
    }

    /// <summary>Full date and time, for tooltips.</summary>
    public static string Full(long unixMs, bool use24Hour, TimeZoneInfo? zone = null)
    {
        var when = ToLocal(unixMs, zone);
        return $"{when.ToString("dddd d MMMM yyyy", English)} {when.ToString(use24Hour ? "HH:mm:ss" : "h:mm:ss tt", English)}";
    }

    public static bool SameLocalDay(long a, long b, TimeZoneInfo? zone = null) =>
        ToLocal(a, zone).Date == ToLocal(b, zone).Date;
}
