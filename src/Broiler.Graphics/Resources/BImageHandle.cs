using System;

namespace Broiler.Graphics;

/// <summary>
/// A typed handle to an image resource owned by a backend. Wraps a <see cref="BResourceHandle"/> and
/// caches the pixel size so layout code can measure without touching the backend.
/// </summary>
public readonly struct BImageHandle : IEquatable<BImageHandle>
{
    public BResourceHandle Handle { get; }
    public BSize PixelSize { get; }

    public BImageHandle(BResourceHandle handle, BSize pixelSize)
    {
        Handle = handle;
        PixelSize = pixelSize;
    }

    public static BImageHandle Invalid => default;

    public bool IsValid => Handle.Kind == BResourceKind.Image && Handle.IsValid;

    /// <summary>Creates an image handle from a raw id; convenience for backends.</summary>
    public static BImageHandle FromId(ulong id, BSize pixelSize) =>
        new(new BResourceHandle(BResourceKind.Image, id), pixelSize);

    public bool Equals(BImageHandle other) =>
        Handle.Equals(other.Handle) && PixelSize.Equals(other.PixelSize);

    public override bool Equals(object? obj) => obj is BImageHandle other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Handle, PixelSize);

    public static bool operator ==(BImageHandle left, BImageHandle right) => left.Equals(right);

    public static bool operator !=(BImageHandle left, BImageHandle right) => !left.Equals(right);

    public override string ToString() => $"BImageHandle({Handle}, {PixelSize})";
}
