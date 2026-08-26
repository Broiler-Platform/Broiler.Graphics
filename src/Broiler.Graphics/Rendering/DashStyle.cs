namespace Broiler.Graphics;

/// <summary>
/// Platform-neutral pen dash style. Values match the historical
/// <c>System.Drawing.Drawing2D.DashStyle</c> layout so existing call sites keep
/// their semantics after the move off the OS-dependent GDI+ types.
/// </summary>
public enum DashStyle
{
    Solid = 0,
    Dash = 1,
    Dot = 2,
    DashDot = 3,
    DashDotDot = 4,
    Custom = 5,
}
