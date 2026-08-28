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

    /// <summary>
    /// The window this one is owned by, or null for an unowned top-level window.
    ///
    /// Ownership is what locks a secondary window's z-order: the window manager keeps an owned
    /// window above its owner, so the owner cannot be raised in front of it, minimizes it with the
    /// owner, and leaves it off the taskbar. That is what a modal dialog needs — input blocking is
    /// the application's business, but staying in front of the window it blocks is the window
    /// manager's, and no amount of activation calls substitutes for it.
    /// </summary>
    public BWindow? Owner { get; init; }
}
