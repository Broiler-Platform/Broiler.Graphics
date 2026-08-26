using System;
using Broiler.Media;

namespace Broiler.Graphics;

/// <summary>
/// Explicit, application-owned registration seam for the image codec catalog that
/// <see cref="BBitmap"/> and the renderers use to decode and encode images.
/// </summary>
/// <remarks>
/// <para>
/// Broiler.Graphics no longer references any concrete codec implementation
/// (<c>Broiler.Media.Image.Managed</c>); it depends only on the <c>Broiler.Media.Image</c>
/// abstraction. The application composition root must register a catalog exactly once at
/// startup, for example:
/// </para>
/// <code>
/// BImageCodecs.Use(new MediaCodecCatalog(ManagedImageCodecs.CreateCodecs()));
/// </code>
/// <para>
/// This is the explicit-registration endpoint called for by the Broiler.Media roadmap
/// (Phase 3 / §8.5): there is no auto-populating default and no hidden managed fallback, so
/// the choice of decoders is always made — and owned — by the composition root, not by the
/// graphics core.
/// </para>
/// </remarks>
public static class BImageCodecs
{
    private static MediaCodecCatalog? _catalog;

    /// <summary>
    /// Registers the image codec catalog for the process. The most recent registration wins,
    /// which keeps the call idempotent for repeated composition roots (e.g. test hosts).
    /// </summary>
    public static void Use(MediaCodecCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    /// <summary>True once a catalog has been registered via <see cref="Use"/>.</summary>
    public static bool IsRegistered => _catalog is not null;

    /// <summary>
    /// The registered image codec catalog. Throws <see cref="InvalidOperationException"/> when
    /// the composition root has not yet called <see cref="Use"/>.
    /// </summary>
    public static MediaCodecCatalog Catalog =>
        _catalog ?? throw new InvalidOperationException(
            "No image codec catalog has been registered with Broiler.Graphics. The application " +
            "composition root must call BImageCodecs.Use(...) before decoding or encoding images, " +
            "e.g. BImageCodecs.Use(new MediaCodecCatalog(ManagedImageCodecs.CreateCodecs())).");
}
