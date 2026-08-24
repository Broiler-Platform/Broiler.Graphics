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
}
