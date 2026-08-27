namespace Broiler.Graphics;

/// <summary>Who draws a window's title bar, border, and system buttons.</summary>
public enum BWindowChrome
{
    /// <summary>The window manager draws the frame — the platform title bar and buttons.</summary>
    System = 0,

    /// <summary>
    /// The frame is suppressed and the whole window is client area, so the application draws its
    /// own title bar and system buttons. Resizing and snapping stay with the window manager;
    /// moving is driven by <see cref="BWindow.BeginMoveDrag"/>.
    /// </summary>
    Owner,
}
