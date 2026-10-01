using Parley.Core;
using Xunit;

namespace Parley.Tests;

public class TimeTextTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    // Wednesday 30 September 2026, 15:00 UTC.
    private static readonly long Now = At(2026, 9, 30, 15, 0);

    private static long At(int year, int month, int day, int hour, int minute) =>
        new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

    [Fact]
    public void Clock_follows_the_12_or_24_hour_setting()
    {
        Assert.Equal("14:05", TimeText.Clock(At(2026, 9, 30, 14, 5), use24Hour: true, Utc));
        Assert.Equal("2:05 PM", TimeText.Clock(At(2026, 9, 30, 14, 5), use24Hour: false, Utc));
        Assert.Equal("00:30", TimeText.Clock(At(2026, 9, 30, 0, 30), use24Hour: true, Utc));
        Assert.Equal("12:30 AM", TimeText.Clock(At(2026, 9, 30, 0, 30), use24Hour: false, Utc));
    }

    [Fact]
    public void The_sidebar_stamp_gets_coarser_the_older_the_message()
    {
        Assert.Equal("14:05", TimeText.Sidebar(At(2026, 9, 30, 14, 5), Now, true, Utc));
        Assert.Equal("Yesterday", TimeText.Sidebar(At(2026, 9, 29, 23, 59), Now, true, Utc));
        Assert.Equal("Sun", TimeText.Sidebar(At(2026, 9, 27, 18, 20), Now, true, Utc));
        Assert.Equal("20 Sep", TimeText.Sidebar(At(2026, 9, 20, 18, 20), Now, true, Utc));
        Assert.Equal("30 Sep 25", TimeText.Sidebar(At(2025, 9, 30, 18, 20), Now, true, Utc));
    }

    [Fact]
    public void A_conversation_with_no_activity_has_no_stamp()
    {
        Assert.Equal(string.Empty, TimeText.Sidebar(0, Now, true, Utc));
    }

    [Fact]
    public void Dividers_say_which_day_as_well_as_when()
    {
        Assert.Equal("Today 14:05", TimeText.Divider(At(2026, 9, 30, 14, 5), Now, true, Utc));
        Assert.Equal("Yesterday 09:15", TimeText.Divider(At(2026, 9, 29, 9, 15), Now, true, Utc));
        Assert.Equal("Sunday 18:20", TimeText.Divider(At(2026, 9, 27, 18, 20), Now, true, Utc));
        Assert.Equal("Sun 20 Sep 18:20", TimeText.Divider(At(2026, 9, 20, 18, 20), Now, true, Utc));
        Assert.Equal("30 Sep 2025 6:20 PM", TimeText.Divider(At(2025, 9, 30, 18, 20), Now, false, Utc));
    }

    [Fact]
    public void Days_are_counted_by_the_calendar_not_by_elapsed_hours()
    {
        // Half an hour apart, but either side of midnight.
        var justAfterMidnight = At(2026, 9, 30, 0, 10);

        Assert.Equal("Yesterday", TimeText.Sidebar(At(2026, 9, 29, 23, 40), justAfterMidnight, true, Utc));
        Assert.False(TimeText.SameLocalDay(At(2026, 9, 29, 23, 40), justAfterMidnight, Utc));
        Assert.True(TimeText.SameLocalDay(At(2026, 9, 30, 0, 1), justAfterMidnight, Utc));
    }

    [Fact]
    public void The_time_zone_decides_which_day_a_message_falls_on()
    {
        var tokyo = TimeZoneInfo.CreateCustomTimeZone("test+9", TimeSpan.FromHours(9), "test+9", "test+9");

        // 20:00 UTC on the 29th is already 05:00 on the 30th, nine hours east.
        var message = At(2026, 9, 29, 20, 0);
        var earlyOnThe30th = At(2026, 9, 30, 3, 0);

        Assert.Equal("Yesterday", TimeText.Sidebar(message, earlyOnThe30th, true, Utc));
        Assert.Equal("05:00", TimeText.Sidebar(message, earlyOnThe30th, true, tokyo));
    }

    [Fact]
    public void The_full_form_is_unambiguous()
    {
        Assert.Equal("Wednesday 30 September 2026 14:05:00", TimeText.Full(At(2026, 9, 30, 14, 5), true, Utc));
    }
}
