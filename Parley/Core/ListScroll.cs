namespace Parley.Core;

/// <summary>
/// Where a list of messages is scrolled to, measured from its end.
///
/// A chat list grows at the bottom, and has older messages added and dropped
/// at the top. Measured from the top, every one of those changes moves what is
/// on screen and has to be compensated for. Measured from the end, additions
/// and removals at the top change nothing at all, and the only case left is a
/// new message arriving while the view is further up, which <see cref="Append"/>
/// handles.
///
/// All values are in pixels. "Total" is the height of the whole list and
/// "view" the height of the part of the window it shows through.
/// </summary>
public struct ListScroll
{
    /// <summary>Closer to the end than this counts as being at it.</summary>
    private const float SnapDistance = 1f;

    /// <summary>How far the view is from the newest message. Zero means following the conversation.</summary>
    public float Back { get; private set; }

    /// <summary>True while the view is on the newest message and should stay there as more arrive.</summary>
    public readonly bool Following => Back <= 0f;

    /// <summary>The furthest the view can be from the end: the part of the list that does not fit in it.</summary>
    public static float MaxBack(float total, float view) => MathF.Max(0f, total - view);

    /// <summary>Goes back to following the newest message.</summary>
    public void ToEnd() => Back = 0f;

    /// <summary>
    /// Call when messages of this combined height have been added at the end.
    /// A view that is following stays on the end. One that is further up stays
    /// on what it was showing, which is now that much further from the end.
    /// </summary>
    public void Append(float height)
    {
        if (Back > 0f && height > 0f) Back += height;
    }

    /// <summary>Moves the view by a number of wheel notches. Positive is towards older messages.</summary>
    public void Wheel(float notches, float step) => Back += notches * step;

    /// <summary>
    /// Scrolls so that the point <paramref name="y"/> pixels down the list is
    /// at the top of the view. If that point is within a view's height of the
    /// end, the view simply goes to the end.
    /// </summary>
    public void ShowAtTop(float y, float total, float view)
    {
        Back = total - view - y;
        Clamp(total, view);
    }

    /// <summary>Brings the position back into range after the list or the view has changed size.</summary>
    public void Clamp(float total, float view)
    {
        var max = MaxBack(total, view);
        if (!float.IsFinite(Back) || Back < SnapDistance) Back = 0f;
        else if (Back > max) Back = max < SnapDistance ? 0f : max;
    }

    /// <summary>
    /// Where the top of the list is, relative to the top of the view. Negative
    /// when the list runs off above it. A list shorter than the view starts at
    /// the top of it rather than hanging from the bottom.
    /// </summary>
    public readonly float ContentTop(float total, float view) =>
        total <= view ? 0f : view - total + Back;

    // ------------------------------------------------------------------
    // Scrollbar
    // ------------------------------------------------------------------

    /// <summary>How tall the scrollbar's thumb is: the visible share of the list, but never too small to grab.</summary>
    public static float ThumbHeight(float track, float total, float view, float minimum)
    {
        if (track <= 0f) return 0f;
        if (total <= view || total <= 0f) return track;
        return Math.Clamp(track * (view / total), MathF.Min(minimum, track), track);
    }

    /// <summary>How far the thumb's top is from the top of the track. At the end of the list the thumb is at the bottom.</summary>
    public readonly float ThumbTop(float track, float thumb, float total, float view)
    {
        var travel = track - thumb;
        var max = MaxBack(total, view);
        if (travel <= 0f || max <= 0f) return 0f;
        return travel * (1f - (Math.Clamp(Back, 0f, max) / max));
    }

    /// <summary>Scrolls to wherever puts the thumb's top this far from the top of the track.</summary>
    public void DragThumb(float thumbTop, float track, float thumb, float total, float view)
    {
        var travel = track - thumb;
        var max = MaxBack(total, view);
        if (travel <= 0f || max <= 0f)
        {
            Back = 0f;
            return;
        }

        Back = (1f - (Math.Clamp(thumbTop, 0f, travel) / travel)) * max;
    }
}
