namespace Broiler.Graphics.Windows;

/// <summary>
/// The kind of change reported by an <see cref="HwndVideoOutput"/> when the window it
/// presents into is resized, shown/hidden, or destroyed by its owner.
/// </summary>
public enum HwndVideoTargetChangeKind
{
    Resized,
    VisibilityChanged,
    Destroyed,
}
