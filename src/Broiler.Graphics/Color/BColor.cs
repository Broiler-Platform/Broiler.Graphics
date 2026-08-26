using System;
using System.Globalization;

namespace Broiler.Graphics;

/// <summary>
/// A non-premultiplied straight-alpha RGBA color with 8 bits per channel.
/// <para>
/// The API deliberately mirrors <c>System.Drawing.Color</c> (alpha-first
/// <see cref="FromArgb(int,int,int,int)"/> overloads, <see cref="FromName"/>,
/// an <see cref="Empty"/> sentinel distinct from transparent-black, and the
/// common named colors) so engine code can use it as a drop-in replacement
/// without the Windows-only <c>System.Drawing.Common</c> dependency.
/// </para>
/// </summary>
public readonly partial struct BColor : IEquatable<BColor>
{
    // Distinguishes the Empty sentinel (default(BColor)) from a real color that
    // happens to be (0,0,0,0). Mirrors System.Drawing.Color, where
    // Color.Empty != Color.FromArgb(0, 0, 0, 0).
    private readonly bool _valid;

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
        _valid = true;
    }

    /// <summary>
    /// The uninitialized sentinel — <c>default(BColor)</c>. <see cref="IsEmpty"/>
    /// is <see langword="true"/>; it is not equal to any real color, including
    /// transparent black. Matches <c>System.Drawing.Color.Empty</c>.
    /// </summary>
    public static BColor Empty => default;

    /// <summary>True for the <see cref="Empty"/> sentinel (an unset color).</summary>
    public bool IsEmpty => !_valid;

    public static BColor FromRgba(byte r, byte g, byte b, byte a) => new(r, g, b, a);

    /// <summary>Builds a color from a packed 0xAARRGGBB value.</summary>
    public static BColor FromArgb(uint argb) => new(
        (byte)((argb >> 16) & 0xFF),
        (byte)((argb >> 8) & 0xFF),
        (byte)(argb & 0xFF),
        (byte)((argb >> 24) & 0xFF));

    /// <summary>
    /// Builds a color from a packed 0xAARRGGBB value. Mirrors
    /// <c>System.Drawing.Color.FromArgb(int)</c>.
    /// </summary>
    public static BColor FromArgb(int argb) => FromArgb(unchecked((uint)argb));

    /// <summary>
    /// Builds an opaque color from red, green, and blue components (0–255).
    /// Mirrors <c>System.Drawing.Color.FromArgb(int, int, int)</c>. Out-of-range
    /// components are clamped rather than throwing.
    /// </summary>
    public static BColor FromArgb(int red, int green, int blue) =>
        new(Clamp(red), Clamp(green), Clamp(blue));

    /// <summary>
    /// Builds a color from alpha, red, green, and blue components (0–255).
    /// Note the alpha-first order, mirroring
    /// <c>System.Drawing.Color.FromArgb(int, int, int, int)</c>. Out-of-range
    /// components are clamped rather than throwing.
    /// </summary>
    public static BColor FromArgb(int alpha, int red, int green, int blue) =>
        new(Clamp(red), Clamp(green), Clamp(blue), Clamp(alpha));

    /// <summary>
    /// Resolves a CSS/X11 color name (case-insensitive) to a color, mirroring
    /// <c>System.Drawing.Color.FromName</c>. Unknown names return
    /// <see cref="Empty"/> (whose <c>A == 0</c>), matching the sentinel behavior
    /// callers rely on. Implemented in the partial <c>BColor.Named.cs</c>.
    /// </summary>
    public static BColor FromName(string name) =>
        TryGetNamedColor(name, out var color) ? color : Empty;

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
        _valid == other._valid &&
        R == other.R && G == other.G && B == other.B && A == other.A;

    public override bool Equals(object? obj) => obj is BColor other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(_valid, R, G, B, A);

    public static bool operator ==(BColor left, BColor right) => left.Equals(right);

    public static bool operator !=(BColor left, BColor right) => !left.Equals(right);

    public override string ToString() =>
        IsEmpty
            ? "BColor [Empty]"
            : string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}{3:X2}", A, R, G, B);

    private static byte Clamp(int v) => (byte)(v < 0 ? 0 : v > 255 ? 255 : v);
}
