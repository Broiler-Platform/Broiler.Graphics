using System;
using System.Diagnostics.CodeAnalysis;
using Broiler.Media;
using Broiler.Media.Image;

namespace Broiler.Graphics;

/// <summary>How a <see cref="BImageResource"/> holds its image.</summary>
public enum BImagePayloadKind
{
    /// <summary>Compressed bytes in a named format, exactly as some source stored them.</summary>
    Encoded,

    /// <summary>Straight-alpha RGBA samples, with no encoding attached.</summary>
    Decoded,
}

/// <summary>
/// An immutable image, either as the bytes a source stored or as the pixels a
/// decoder produced, together with the intrinsic size a bounded inspection could
/// establish.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why the two forms are one type and not two.</strong> A document
/// carries images from both directions. A picture pasted from a file arrives as
/// encoded bytes and should reach the output byte-identical rather than
/// re-encoded; a picture recovered from inside a container arrives as samples a
/// decoder produced and has no encoding at all. Consumers care about the same
/// four things either way — how big it is, what it holds, whether it can be
/// written out unchanged, and whether it can be drawn — so the discriminant
/// belongs in the type rather than in every consumer's type test.
/// </para>
/// <para>
/// <strong>An encoding is never fabricated.</strong>
/// <see cref="TryGetEncoded"/> answers false for a decoded resource. It would be
/// easy to re-encode on demand and return something, and it would be wrong twice
/// over: the bytes would not be the document's, and a lossy round trip would
/// quietly change the picture. A caller that needs bytes for a decoded resource
/// has to encode them itself, at a moment and a quality it chose.
/// </para>
/// <para>
/// <strong>Intrinsic size may be unknown.</strong>
/// <see cref="PixelWidth"/> and <see cref="PixelHeight"/> are null when no
/// inspection established them — an encoded payload in a format no registered
/// codec recognizes, or one whose header does not hold together. Null is a
/// distinct answer from zero, and callers are expected to treat it as "this
/// resource cannot be placed at its natural size" rather than as a default.
/// </para>
/// <para>
/// <strong>Buffer ownership.</strong> This type never copies a payload and never
/// mutates one. An encoded resource holds the caller's
/// <see cref="ReadOnlyMemory{T}"/> and a decoded one holds the caller's
/// <see cref="BPixelBuffer"/>; in both cases the resource takes ownership at
/// construction, and a caller that mutates the backing storage afterwards has
/// broken this type's immutability rather than found a feature. Pass a copy when
/// the buffer is still yours.
/// </para>
/// </remarks>
public sealed class BImageResource
{
    private readonly ReadOnlyMemory<byte> _encoded;
    private readonly BPixelBuffer? _pixels;
    private readonly string? _mediaType;

    private BImageResource(
        BImagePayloadKind kind,
        ReadOnlyMemory<byte> encoded,
        BPixelBuffer? pixels,
        string? mediaType,
        int? pixelWidth,
        int? pixelHeight)
    {
        Kind = kind;
        _encoded = encoded;
        _pixels = pixels;
        _mediaType = mediaType;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
    }

    /// <summary>Which of the two payloads this resource holds.</summary>
    public BImagePayloadKind Kind { get; }

    /// <summary>
    /// Intrinsic width in pixels, or null when nothing established it. For a
    /// decoded resource this is always the buffer's own width.
    /// </summary>
    public int? PixelWidth { get; }

    /// <summary>Intrinsic height in pixels, or null when nothing established it.</summary>
    public int? PixelHeight { get; }

    /// <summary>
    /// True when both intrinsic dimensions are known, which is what a caller
    /// needs before it can place the image at its natural size.
    /// </summary>
    [MemberNotNullWhen(true, nameof(PixelWidth), nameof(PixelHeight))]
    public bool HasIntrinsicSize => PixelWidth is not null && PixelHeight is not null;

    /// <summary>
    /// The IANA media type of an encoded payload, or null for a decoded one.
    /// Decoded samples have no encoding, and this does not invent one.
    /// </summary>
    public string? MediaType => _mediaType;

    /// <summary>Encoded bytes, in the format named by <paramref name="mediaType"/>.</summary>
    /// <remarks>
    /// The size is taken on trust from the caller, which is the right shape when
    /// the source document already stated it — a DOCX drawing and a PDF image
    /// dictionary both do, and their word beats a re-inspection of the payload.
    /// Pass nulls when the source said nothing and let
    /// <see cref="FromEncoded(ReadOnlyMemory{byte}, string)"/> inspect instead.
    /// </remarks>
    public static BImageResource FromEncoded(
        ReadOnlyMemory<byte> bytes,
        string mediaType,
        int? pixelWidth,
        int? pixelHeight)
    {
        if (string.IsNullOrWhiteSpace(mediaType))
            throw new ArgumentException("An encoded image resource names its media type.", nameof(mediaType));
        if (pixelWidth is <= 0)
            throw new ArgumentOutOfRangeException(nameof(pixelWidth), pixelWidth, "A stated pixel width is positive.");
        if (pixelHeight is <= 0)
            throw new ArgumentOutOfRangeException(nameof(pixelHeight), pixelHeight, "A stated pixel height is positive.");

        return new BImageResource(BImagePayloadKind.Encoded, bytes, null, mediaType, pixelWidth, pixelHeight);
    }

    /// <summary>
    /// Encoded bytes whose intrinsic size is established by inspecting them.
    /// </summary>
    /// <remarks>
    /// The inspection is bounded and decodes nothing: it reads the format's
    /// header through the registered codec catalog. When no catalog is
    /// registered, or no codec recognizes the bytes, or the header does not hold
    /// together, the resource is still created and its intrinsic size is null —
    /// an unreadable header is a fact about the image, not a reason to refuse to
    /// carry it.
    /// </remarks>
    public static BImageResource FromEncoded(ReadOnlyMemory<byte> bytes, string mediaType)
    {
        ImageInfo? info = Inspect(bytes.Span);
        return FromEncoded(bytes, mediaType, info?.Width, info?.Height);
    }

    /// <summary>Decoded RGBA samples, whose intrinsic size is the buffer's own.</summary>
    public static BImageResource FromPixels(BPixelBuffer pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        return new BImageResource(BImagePayloadKind.Decoded, default, pixels, null, pixels.Width, pixels.Height);
    }

    /// <summary>
    /// The encoded bytes and their media type, when this resource has them.
    /// </summary>
    /// <returns>
    /// False for a decoded resource. Nothing is encoded to satisfy the call: see
    /// the type remarks for why re-encoding here would be the wrong answer.
    /// </returns>
    public bool TryGetEncoded(out ReadOnlyMemory<byte> bytes, [NotNullWhen(true)] out string? mediaType)
    {
        if (Kind == BImagePayloadKind.Encoded && _mediaType is not null)
        {
            bytes = _encoded;
            mediaType = _mediaType;
            return true;
        }

        bytes = default;
        mediaType = null;
        return false;
    }

    /// <summary>
    /// The decoded samples, when this resource holds them. False for an encoded
    /// resource: decoding is a bounded operation with its own limits and its own
    /// failure modes, and a property getter is the wrong place to start one.
    /// </summary>
    public bool TryGetPixels([NotNullWhen(true)] out BPixelBuffer? pixels)
    {
        pixels = _pixels;
        return pixels is not null;
    }

    /// <summary>
    /// Reads the header through the registered catalog, or returns null when
    /// there is no catalog, no codec for these bytes, or no readable header.
    /// </summary>
    private static ImageInfo? Inspect(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty || !BImageCodecs.IsRegistered)
            return null;

        foreach (MediaCodec codec in BImageCodecs.Catalog.Codecs)
        {
            if (codec is ImageCodec image && image.TryInspect(bytes, out ImageInfo? info))
                return info;
        }

        return null;
    }
}
