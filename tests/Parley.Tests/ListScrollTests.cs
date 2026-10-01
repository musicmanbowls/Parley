using Parley.Core;
using Xunit;

namespace Parley.Tests;

public class ListScrollTests
{
    private const float View = 300f;

    /// <summary>Where something <paramref name="y"/> pixels down the list ends up, relative to the top of the view.</summary>
    private static float OnScreen(ListScroll scroll, float y, float total) => scroll.ContentTop(total, View) + y;

    [Fact]
    public void A_new_list_follows_the_end()
    {
        var scroll = default(ListScroll);

        Assert.True(scroll.Following);
        Assert.Equal(0f, scroll.Back);
    }

    [Fact]
    public void A_list_shorter_than_the_view_starts_at_the_top()
    {
        var scroll = default(ListScroll);
        scroll.Clamp(120f, View);

        Assert.Equal(0f, scroll.ContentTop(120f, View));
        Assert.Equal(0f, ListScroll.MaxBack(120f, View));
    }

    [Fact]
    public void Following_keeps_the_end_of_the_list_on_the_bottom_edge()
    {
        var scroll = default(ListScroll);

        foreach (var total in new[] { 301f, 900f, 5000f })
        {
            scroll.Clamp(total, View);
            Assert.Equal(View, scroll.ContentTop(total, View) + total);
        }
    }

    [Fact]
    public void A_message_arriving_while_following_is_shown_at_once()
    {
        var scroll = default(ListScroll);
        var total = 1000f;
        scroll.Clamp(total, View);

        scroll.Append(40f);
        total += 40f;
        scroll.Clamp(total, View);

        Assert.True(scroll.Following);
        Assert.Equal(View, scroll.ContentTop(total, View) + total);
    }

    [Fact]
    public void A_message_arriving_while_reading_further_up_moves_nothing()
    {
        var scroll = default(ListScroll);
        var total = 1000f;
        scroll.Wheel(3f, 50f);
        scroll.Clamp(total, View);
        var before = OnScreen(scroll, 600f, total);

        scroll.Append(40f);
        total += 40f;
        scroll.Clamp(total, View);

        Assert.False(scroll.Following);
        Assert.Equal(before, OnScreen(scroll, 600f, total));
    }

    [Fact]
    public void Older_messages_added_at_the_top_move_nothing()
    {
        var scroll = default(ListScroll);
        var total = 1000f;
        scroll.Wheel(4f, 50f);
        scroll.Clamp(total, View);
        var before = OnScreen(scroll, 600f, total);

        // A page of history 750 pixels tall goes in above everything. What
        // was 600 down the list is now 1350 down it.
        total += 750f;
        scroll.Clamp(total, View);

        Assert.Equal(before, OnScreen(scroll, 1350f, total));
    }

    [Fact]
    public void Older_messages_added_at_the_top_move_nothing_while_following_either()
    {
        var scroll = default(ListScroll);
        var total = 1000f;
        scroll.Clamp(total, View);
        var before = OnScreen(scroll, 950f, total);

        total += 750f;
        scroll.Clamp(total, View);

        Assert.Equal(before, OnScreen(scroll, 1700f, total));
    }

    [Fact]
    public void Messages_dropped_from_the_top_move_nothing()
    {
        var scroll = default(ListScroll);
        var total = 2000f;
        scroll.Wheel(4f, 50f);
        scroll.Clamp(total, View);
        var before = OnScreen(scroll, 1600f, total);

        // The oldest 500 pixels are dropped from memory.
        total -= 500f;
        scroll.Clamp(total, View);

        Assert.Equal(before, OnScreen(scroll, 1100f, total));
    }

    [Fact]
    public void Dropping_what_was_being_read_leaves_the_view_at_the_new_top()
    {
        var scroll = default(ListScroll);
        var total = 2000f;
        scroll.Wheel(100f, 50f);
        scroll.Clamp(total, View);
        Assert.Equal(0f, scroll.ContentTop(total, View));

        total -= 500f;
        scroll.Clamp(total, View);

        Assert.Equal(0f, scroll.ContentTop(total, View));
        Assert.Equal(ListScroll.MaxBack(total, View), scroll.Back);
    }

    [Fact]
    public void The_wheel_cannot_scroll_past_either_end()
    {
        var scroll = default(ListScroll);
        const float total = 1000f;

        scroll.Wheel(500f, 50f);
        scroll.Clamp(total, View);
        Assert.Equal(700f, scroll.Back);
        Assert.Equal(0f, scroll.ContentTop(total, View));

        scroll.Wheel(-500f, 50f);
        scroll.Clamp(total, View);
        Assert.True(scroll.Following);
    }

    [Fact]
    public void Scrolling_back_to_within_a_pixel_of_the_end_resumes_following()
    {
        var scroll = default(ListScroll);
        scroll.Wheel(1f, 50f);
        scroll.Wheel(-0.99f, 50f);
        scroll.Clamp(1000f, View);

        Assert.True(scroll.Following);
        Assert.Equal(0f, scroll.Back);
    }

    [Fact]
    public void A_list_that_shrinks_to_fit_the_view_goes_back_to_following()
    {
        var scroll = default(ListScroll);
        scroll.Wheel(5f, 50f);
        scroll.Clamp(1000f, View);
        Assert.False(scroll.Following);

        scroll.Clamp(200f, View);

        Assert.True(scroll.Following);
        Assert.Equal(0f, scroll.ContentTop(200f, View));
    }

    [Fact]
    public void A_position_that_is_not_a_number_is_treated_as_the_end()
    {
        var scroll = default(ListScroll);
        scroll.Wheel(float.NaN, 50f);
        scroll.Clamp(1000f, View);

        Assert.True(scroll.Following);
    }

    [Fact]
    public void ToEnd_resumes_following()
    {
        var scroll = default(ListScroll);
        scroll.Wheel(5f, 50f);
        scroll.Clamp(1000f, View);

        scroll.ToEnd();

        Assert.True(scroll.Following);
    }

    [Fact]
    public void ShowAtTop_puts_that_point_on_the_top_edge()
    {
        var scroll = default(ListScroll);
        const float total = 2000f;

        scroll.ShowAtTop(800f, total, View);

        Assert.Equal(0f, OnScreen(scroll, 800f, total));
        Assert.False(scroll.Following);
    }

    [Fact]
    public void ShowAtTop_near_the_end_just_goes_to_the_end()
    {
        var scroll = default(ListScroll);
        const float total = 2000f;

        // Less than a view's height from the end: it is on screen anyway.
        scroll.ShowAtTop(1850f, total, View);

        Assert.True(scroll.Following);
    }

    [Fact]
    public void ShowAtTop_on_a_short_list_changes_nothing()
    {
        var scroll = default(ListScroll);
        scroll.ShowAtTop(50f, 200f, View);

        Assert.True(scroll.Following);
        Assert.Equal(0f, scroll.ContentTop(200f, View));
    }

    [Fact]
    public void The_thumb_is_the_visible_share_of_the_track()
    {
        Assert.Equal(75f, ListScroll.ThumbHeight(300f, 1200f, 300f, 24f));
    }

    [Fact]
    public void The_thumb_never_gets_too_small_to_grab()
    {
        Assert.Equal(24f, ListScroll.ThumbHeight(300f, 100_000f, 300f, 24f));
    }

    [Fact]
    public void The_thumb_fills_the_track_when_everything_fits()
    {
        Assert.Equal(300f, ListScroll.ThumbHeight(300f, 200f, 300f, 24f));
        Assert.Equal(300f, ListScroll.ThumbHeight(300f, 0f, 300f, 24f));
    }

    [Fact]
    public void The_thumb_fits_a_track_shorter_than_its_minimum()
    {
        Assert.Equal(10f, ListScroll.ThumbHeight(10f, 100_000f, 10f, 24f));
        Assert.Equal(0f, ListScroll.ThumbHeight(0f, 100f, 10f, 24f));
    }

    [Fact]
    public void The_thumb_sits_at_the_bottom_while_following_and_at_the_top_when_scrolled_right_back()
    {
        const float total = 1200f;
        var thumb = ListScroll.ThumbHeight(View, total, View, 24f);
        var scroll = default(ListScroll);

        Assert.Equal(View - thumb, scroll.ThumbTop(View, thumb, total, View));

        scroll.Wheel(1000f, 50f);
        scroll.Clamp(total, View);
        Assert.Equal(0f, scroll.ThumbTop(View, thumb, total, View));
    }

    [Fact]
    public void Dragging_the_thumb_and_reading_its_position_agree()
    {
        const float total = 1200f;
        var thumb = ListScroll.ThumbHeight(View, total, View, 24f);
        var scroll = default(ListScroll);

        foreach (var top in new[] { 0f, 10f, 112.5f, 200f, View - thumb })
        {
            scroll.DragThumb(top, View, thumb, total, View);
            Assert.Equal(top, scroll.ThumbTop(View, thumb, total, View), 3);
        }
    }

    [Fact]
    public void Dragging_the_thumb_past_the_ends_of_the_track_stops_at_them()
    {
        const float total = 1200f;
        var thumb = ListScroll.ThumbHeight(View, total, View, 24f);
        var scroll = default(ListScroll);

        scroll.DragThumb(-500f, View, thumb, total, View);
        Assert.Equal(ListScroll.MaxBack(total, View), scroll.Back);

        scroll.DragThumb(5000f, View, thumb, total, View);
        Assert.Equal(0f, scroll.Back);
    }

    [Fact]
    public void Dragging_the_thumb_to_the_top_shows_the_top_of_the_list()
    {
        const float total = 1200f;
        var thumb = ListScroll.ThumbHeight(View, total, View, 24f);
        var scroll = default(ListScroll);

        scroll.DragThumb(0f, View, thumb, total, View);
        scroll.Clamp(total, View);

        Assert.Equal(0f, scroll.ContentTop(total, View));
    }

    [Fact]
    public void A_thumb_with_nowhere_to_go_leaves_the_view_at_the_end()
    {
        var scroll = default(ListScroll);
        scroll.DragThumb(40f, View, View, 200f, View);

        Assert.True(scroll.Following);
        Assert.Equal(0f, scroll.ThumbTop(View, View, 200f, View));
    }
}
