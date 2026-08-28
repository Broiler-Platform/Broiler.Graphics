namespace Broiler.Graphics;

/// <summary>Platform-neutral settings used when creating a render window.</summary>
public sealed record BWindowOptions
{
    public string Title { get; init; } = "Broiler.Graphics";

    public int ClientWidth { get; init; } = 1024;

    public int ClientHeight { get; init; } = 768;

    public BColor ClearColor { get; init; } = BColor.White;

    public bool EnableTransparency { get; init; }

    public BRenderOptions RenderOptions { get; init; } = BRenderOptions.Default;

    /// <summary>
    /// When true (the default) the window owns the thread's message loop: <see cref="BWindow.Run"/>
    /// blocks until the window closes and closing quits the loop. Set false for a secondary window
    /// realized with <see cref="BWindow.Show"/> that is serviced by an existing loop and whose close
    /// must not quit the application.
    /// </summary>
    public bool OwnsMessageLoop { get; init; } = true;

    /// <summary>
    /// Who draws the title bar and border. <see cref="BWindowChrome.Owner"/> suppresses the
    /// platform frame so the whole window is client area and the application draws its own chrome.
    /// </summary>
    public BWindowChrome Chrome { get; init; } = BWindowChrome.System;

    /// <summary>
    /// Whether the window can be resized and maximized by the user. False produces a fixed-size
    /// window — with <see cref="BWindowChrome.Owner"/> it also disables the resize border.
    /// </summary>
    public bool Resizable { get; init; } = true;

    /// <summary>
    /// Requested left edge in device-independent pixels. Null centers the window on the screen.
    /// </summary>
    public double? Left { get; init; }

    /// <summary>
    /// Requested top edge in device-independent pixels. Null centers the window on the screen.
    /// </summary>
    public double? Top { get; init; }
}
