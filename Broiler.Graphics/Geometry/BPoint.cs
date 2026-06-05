using System;
using System.Globalization;

namespace Broiler.Graphics;

/// <summary>
/// A 2D point in device-independent layout units. Uses <see cref="double"/> for layout precision.
/// </summary>
public readonly struct BPoint : IEquatable<BPoint>
{
    public double X { get; }
    public double Y { get; }

    public BPoint(double x, double y)
    {
        X = x;
        Y = y;
    }

    public static BPoint Zero => new(0, 0);

    public BPoint Offset(double dx, double dy) => new(X + dx, Y + dy);

    public bool Equals(BPoint other) => X.Equals(other.X) && Y.Equals(other.Y);

    public override bool Equals(object? obj) => obj is BPoint other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(X, Y);

    public static bool operator ==(BPoint left, BPoint right) => left.Equals(right);

    public static bool operator !=(BPoint left, BPoint right) => !left.Equals(right);

    public override string ToString() =>
        string.Format(CultureInfo.InvariantCulture, "BPoint({0}, {1})", X, Y);
}
