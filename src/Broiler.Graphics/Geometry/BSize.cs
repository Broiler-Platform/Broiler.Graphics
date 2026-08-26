using System;
using System.Globalization;

namespace Broiler.Graphics;

/// <summary>
/// A 2D size (width/height) in device-independent layout units.
/// </summary>
public readonly struct BSize : IEquatable<BSize>
{
    public double Width { get; }
    public double Height { get; }

    public BSize(double width, double height)
    {
        Width = width;
        Height = height;
    }

    public static BSize Empty => new(0, 0);

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool Equals(BSize other) => Width.Equals(other.Width) && Height.Equals(other.Height);

    public override bool Equals(object? obj) => obj is BSize other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Width, Height);

    public static bool operator ==(BSize left, BSize right) => left.Equals(right);

    public static bool operator !=(BSize left, BSize right) => !left.Equals(right);

    public override string ToString() =>
        string.Format(CultureInfo.InvariantCulture, "BSize({0} x {1})", Width, Height);
}
