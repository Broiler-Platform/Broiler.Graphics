using System;
using System.Globalization;

namespace Broiler.Graphics;

/// <summary>
/// An axis-aligned rectangle in device-independent layout units.
/// </summary>
public readonly struct BRect : IEquatable<BRect>
{
    public double X { get; }
    public double Y { get; }
    public double Width { get; }
    public double Height { get; }

    public BRect(double x, double y, double width, double height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public static BRect FromLTRB(double left, double top, double right, double bottom) =>
        new(left, top, right - left, bottom - top);

    public static BRect Empty => new(0, 0, 0, 0);

    public double Left => X;
    public double Top => Y;
    public double Right => X + Width;
    public double Bottom => Y + Height;

    public BPoint Location => new(X, Y);
    public BSize Size => new(Width, Height);

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool Contains(BPoint point) =>
        point.X >= Left && point.X < Right && point.Y >= Top && point.Y < Bottom;

    /// <summary>Returns the intersection of two rectangles, or <see cref="Empty"/> if they do not overlap.</summary>
    public BRect Intersect(BRect other)
    {
        double left = Math.Max(Left, other.Left);
        double top = Math.Max(Top, other.Top);
        double right = Math.Min(Right, other.Right);
        double bottom = Math.Min(Bottom, other.Bottom);

        return right > left && bottom > top
            ? FromLTRB(left, top, right, bottom)
            : Empty;
    }

    public bool Equals(BRect other) =>
        X.Equals(other.X) && Y.Equals(other.Y) &&
        Width.Equals(other.Width) && Height.Equals(other.Height);

    public override bool Equals(object? obj) => obj is BRect other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(X, Y, Width, Height);

    public static bool operator ==(BRect left, BRect right) => left.Equals(right);

    public static bool operator !=(BRect left, BRect right) => !left.Equals(right);

    public override string ToString() =>
        string.Format(CultureInfo.InvariantCulture, "BRect({0}, {1}, {2}, {3})", X, Y, Width, Height);
}
