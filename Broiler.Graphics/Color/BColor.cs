using System;
using System.Globalization;

namespace Broiler.Graphics;

/// <summary>
/// A non-premultiplied straight-alpha RGBA color with 8 bits per channel.
/// </summary>
public readonly struct BColor : IEquatable<BColor>
{
    public byte R { get; }
    public byte G { get; }
    public byte B { get; }
    public byte A { get; }

    public BColor(byte r, byte g, byte b, byte a = 255)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    public static BColor FromRgba(byte r, byte g, byte b, byte a) => new(r, g, b, a);

    /// <summary>Builds a color from a packed 0xAARRGGBB value.</summary>
    public static BColor FromArgb(uint argb) => new(
        (byte)((argb >> 16) & 0xFF),
        (byte)((argb >> 8) & 0xFF),
        (byte)(argb & 0xFF),
        (byte)((argb >> 24) & 0xFF));

    /// <summary>Packs the color into a 0xAARRGGBB value.</summary>
    public uint ToArgb() =>
        ((uint)A << 24) | ((uint)R << 16) | ((uint)G << 8) | B;

    // Channels normalized to [0, 1] for backend conversion convenience.
    public float Rf => R / 255f;
    public float Gf => G / 255f;
    public float Bf => B / 255f;
    public float Af => A / 255f;

    public static BColor Transparent => new(0, 0, 0, 0);
    public static BColor Black => new(0, 0, 0);
    public static BColor White => new(255, 255, 255);
    public static BColor Red => new(255, 0, 0);
    public static BColor Green => new(0, 128, 0);
    public static BColor Blue => new(0, 0, 255);

    public bool Equals(BColor other) =>
        R == other.R && G == other.G && B == other.B && A == other.A;

    public override bool Equals(object? obj) => obj is BColor other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(R, G, B, A);

    public static bool operator ==(BColor left, BColor right) => left.Equals(right);

    public static bool operator !=(BColor left, BColor right) => !left.Equals(right);

    public override string ToString() =>
        string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}{3:X2}", A, R, G, B);
}
