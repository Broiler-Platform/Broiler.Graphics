namespace Broiler.Graphics;

/// <summary>
/// The edge or corner a resize drag started from, for
/// <see cref="BWindow.BeginResizeDrag(BWindowEdge)"/>. An owner-drawn frame hit-tests its own
/// border and names the edge; the window manager runs the drag.
/// </summary>
public enum BWindowEdge
{
    None = 0,
    Left,
    Top,
    Right,
    Bottom,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}
