using System;

namespace Broiler.Graphics;

/// <summary>
/// Identifies the kind of resource a <see cref="BResourceHandle"/> refers to.
/// </summary>
public enum BResourceKind : byte
{
    None = 0,
    Image = 1,
    Font = 2,
    Brush = 3,
    Surface = 4,
}

/// <summary>
/// An opaque, value-type handle to a backend resource. The Core never dereferences it; backends map
/// the <see cref="Id"/> to their own native object table. Equality is by (kind, id).
/// </summary>
public readonly struct BResourceHandle : IEquatable<BResourceHandle>
{
    public BResourceKind Kind { get; }
    public ulong Id { get; }

    public BResourceHandle(BResourceKind kind, ulong id)
    {
        Kind = kind;
        Id = id;
    }

    public static BResourceHandle None => default;

    public bool IsValid => Kind != BResourceKind.None && Id != 0;

    public bool Equals(BResourceHandle other) => Kind == other.Kind && Id == other.Id;

    public override bool Equals(object? obj) => obj is BResourceHandle other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Kind, Id);

    public static bool operator ==(BResourceHandle left, BResourceHandle right) => left.Equals(right);

    public static bool operator !=(BResourceHandle left, BResourceHandle right) => !left.Equals(right);

    public override string ToString() => $"BResourceHandle({Kind}, #{Id})";
}
