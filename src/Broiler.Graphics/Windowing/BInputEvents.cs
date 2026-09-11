using Broiler.Graphics.Geometry;
using System;

namespace Broiler.Graphics.Windowing;

/// <summary>Mouse buttons, expressed as a bit flag so multiple held buttons can be reported together.</summary>
[Flags]
public enum BMouseButtons
{
    None = 0,
    Left = 1,
    Right = 2,
    Middle = 4,
}

/// <summary>
/// A pointer (mouse) event. <see cref="Position"/> is in device-independent units relative to the
/// top-left of the window's render content area (i.e. excluding any chrome the host reserves).
/// </summary>
public readonly struct BPointerEventArgs(BPoint position, BMouseButtons buttons, BMouseButtons changedButton = BMouseButtons.None)
{
    public BPoint Position { get; } = position;

    public BMouseButtons Buttons { get; } = buttons;

    /// <summary>The mouse button that caused a button down/up event, or <see cref="BMouseButtons.None"/> otherwise.</summary>
    public BMouseButtons ChangedButton { get; } = changedButton;

    public bool LeftButton => (Buttons & BMouseButtons.Left) != 0;

    public bool RightButton => (Buttons & BMouseButtons.Right) != 0;

    public bool MiddleButton => (Buttons & BMouseButtons.Middle) != 0;
}

/// <summary>
/// A mouse-wheel event. <see cref="Delta"/> is in wheel notches: positive scrolls up / away from the
/// user on the vertical axis, and to the right on the horizontal one. <see cref="Position"/> matches
/// <see cref="BPointerEventArgs.Position"/>.
/// </summary>
/// <remarks>
/// The modifier keys are carried because the platform hands them over with the notch and they decide
/// what the notch means: shift and a wheel is how every editor scrolls sideways on a mouse that has
/// one wheel. <see cref="BKeyEventArgs"/> has carried the same three from the start; a wheel event
/// that dropped them left a host unable to tell the two gestures apart, however plainly the user had
/// made the distinction.
/// </remarks>
public readonly struct BMouseWheelEventArgs(BPoint position, double delta, BMouseButtons buttons,
    bool control = false, bool shift = false, bool alt = false, bool isHorizontal = false)
{
    public BPoint Position { get; } = position;

    public double Delta { get; } = delta;

    public BMouseButtons Buttons { get; } = buttons;

    public bool Control { get; } = control;

    public bool Shift { get; } = shift;

    public bool Alt { get; } = alt;

    /// <summary>True for a wheel that tilts, or a touchpad's sideways gesture.</summary>
    public bool IsHorizontal { get; } = isHorizontal;
}

/// <summary>A keyboard key event. <see cref="VirtualKey"/> is the platform virtual-key code.</summary>
public readonly struct BKeyEventArgs(int virtualKey, bool control, bool shift, bool alt)
{
    public int VirtualKey { get; } = virtualKey;

    public bool Control { get; } = control;

    public bool Shift { get; } = shift;

    public bool Alt { get; } = alt;
}

/// <summary>A text-input event carrying a single translated character (e.g. from WM_CHAR).</summary>
public readonly struct BTextInputEventArgs(char character)
{
    public char Character { get; } = character;
}

/// <summary>Common virtual-key codes, kept backend-neutral so host apps need not reference Win32.</summary>
public static class BVirtualKey
{
    public const int Back = 0x08;
    public const int Tab = 0x09;
    public const int Enter = 0x0D;
    public const int Escape = 0x1B;
    public const int Space = 0x20;
    public const int A = 0x41;
    public const int C = 0x43;
    public const int PageUp = 0x21;
    public const int PageDown = 0x22;
    public const int End = 0x23;
    public const int Home = 0x24;
    public const int Left = 0x25;
    public const int Up = 0x26;
    public const int Right = 0x27;
    public const int Down = 0x28;
    public const int F5 = 0x74;
    public const int F12 = 0x7B;
}
