using System;
using System.Drawing;
using Broiler.Graphics;

namespace Broiler.Graphics;

public abstract class RImage : IDisposable
{
    public abstract double Width { get; }
    public abstract double Height { get; }

    /// <summary>
    /// The intrinsic width when available. Defaults to the decoded image width.
    /// </summary>
    public virtual double IntrinsicWidth => Width;

    /// <summary>
    /// The intrinsic height when available. Defaults to the decoded image height.
    /// </summary>
    public virtual double IntrinsicHeight => Height;

    /// <summary>
    /// The intrinsic aspect ratio (width ÷ height) when available.
    /// Defaults to the decoded bitmap ratio.
    /// </summary>
    public virtual double IntrinsicAspectRatio => Height > 0 ? Width / Height : 0;

    /// <summary>
    /// Whether the image has an intrinsic aspect ratio.  Raster images
    /// always have one (width÷height of the bitmap).  SVGs without a
    /// viewBox have no intrinsic ratio, which affects how CSS min/max
    /// width/height constraints are applied (CSS Images Module Level 3
    /// §5.2).  Defaults to <c>true</c>.
    /// </summary>
    public virtual bool HasIntrinsicRatio => true;

    /// <summary>
    /// Whether the image has an intrinsic width (e.g. a raster image or an
    /// SVG with an explicit <c>width</c> attribute).  Defaults to <c>true</c>.
    /// </summary>
    public virtual bool HasIntrinsicWidth => true;

    /// <summary>
    /// Whether the image has an intrinsic height (e.g. a raster image or an
    /// SVG with an explicit <c>height</c> attribute).  Defaults to <c>true</c>.
    /// </summary>
    public virtual bool HasIntrinsicHeight => true;

    /// <summary>
    /// Attempts to report a single uniform color for the entire image.
    /// Returns <c>false</c> when the adapter cannot determine a uniform color
    /// or when the image contains multiple colors.
    /// </summary>
    public virtual bool TryGetUniformColor(out BColor color)
    {
        color = BColor.Empty;
        return false;
    }

    /// <summary>
    /// Attempts to sample a representative color from a source rectangle within
    /// the image. Defaults to the uniform-color fast path when available.
    /// </summary>
    public virtual bool TryGetSampledColor(RectangleF sourceRect, out BColor color)
        => TryGetUniformColor(out color);

    public abstract void Dispose();
}
