using System;
using System.Globalization;

namespace Broiler.Graphics;

/// <summary>
/// A 2D affine transform represented as a 3x2 matrix:
/// <code>
/// | M11 M12 |
/// | M21 M22 |
/// | M31 M32 |
/// </code>
/// where (M31, M32) is the translation. Uses <see cref="double"/> for layout precision;
/// backends may convert to <c>float</c>.
/// </summary>
public readonly struct BMatrix3x2 : IEquatable<BMatrix3x2>
{
    public double M11 { get; }
    public double M12 { get; }
    public double M21 { get; }
    public double M22 { get; }
    public double M31 { get; }
    public double M32 { get; }

    public BMatrix3x2(double m11, double m12, double m21, double m22, double m31, double m32)
    {
        M11 = m11;
        M12 = m12;
        M21 = m21;
        M22 = m22;
        M31 = m31;
        M32 = m32;
    }

    public static BMatrix3x2 Identity => new(1, 0, 0, 1, 0, 0);

    public bool IsIdentity =>
        M11 == 1 && M12 == 0 && M21 == 0 && M22 == 1 && M31 == 0 && M32 == 0;

    public static BMatrix3x2 Translation(double dx, double dy) => new(1, 0, 0, 1, dx, dy);

    public static BMatrix3x2 Scale(double sx, double sy) => new(sx, 0, 0, sy, 0, 0);

    /// <summary>Concatenates two transforms (this then <paramref name="other"/>).</summary>
    public BMatrix3x2 Multiply(BMatrix3x2 other) => new(
        M11 * other.M11 + M12 * other.M21,
        M11 * other.M12 + M12 * other.M22,
        M21 * other.M11 + M22 * other.M21,
        M21 * other.M12 + M22 * other.M22,
        M31 * other.M11 + M32 * other.M21 + other.M31,
        M31 * other.M12 + M32 * other.M22 + other.M32);

    public BPoint Transform(BPoint point) => new(
        point.X * M11 + point.Y * M21 + M31,
        point.X * M12 + point.Y * M22 + M32);

    public static BMatrix3x2 operator *(BMatrix3x2 left, BMatrix3x2 right) => left.Multiply(right);

    public bool Equals(BMatrix3x2 other) =>
        M11.Equals(other.M11) && M12.Equals(other.M12) &&
        M21.Equals(other.M21) && M22.Equals(other.M22) &&
        M31.Equals(other.M31) && M32.Equals(other.M32);

    public override bool Equals(object? obj) => obj is BMatrix3x2 other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(M11, M12, M21, M22, M31, M32);

    public static bool operator ==(BMatrix3x2 left, BMatrix3x2 right) => left.Equals(right);

    public static bool operator !=(BMatrix3x2 left, BMatrix3x2 right) => !left.Equals(right);

    public override string ToString() => string.Format(
        CultureInfo.InvariantCulture,
        "BMatrix3x2[{0}, {1}, {2}, {3}, {4}, {5}]", M11, M12, M21, M22, M31, M32);
}
