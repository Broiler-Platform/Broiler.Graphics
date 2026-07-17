using System.Drawing;
using System;
using System.Collections.Generic;

namespace Broiler.Graphics;

public abstract class RGraphics : IDisposable
{
    protected readonly IResourceFactory _adapter;
    protected readonly Stack<RectangleF> _clipStack = new();

    protected RGraphics(IResourceFactory adapter, RectangleF initialClip)
    {
        ArgumentNullException.ThrowIfNull(adapter);

        _adapter = adapter;
        _clipStack.Push(initialClip);
    }

    public RPen GetPen(BColor color) => _adapter.GetPen(color);
    public RBrush GetSolidBrush(BColor color) => _adapter.GetSolidBrush(color);
    public RBrush GetLinearGradientBrush(RectangleF rect, BColor color1, BColor color2, double angle) => _adapter.GetLinearGradientBrush(rect, color1, color2, angle);
    public RectangleF GetClip() => _clipStack.Peek();
    public abstract void PopClip();
    public abstract void PushClip(RectangleF rect);
    public abstract void PushClipExclude(RectangleF rect);

    /// <summary>
    /// Pushes a rounded-rectangle clip onto the clip stack.
    /// Default implementation falls back to a rectangular clip.
    /// </summary>
    public virtual void PushClipRounded(RectangleF rect,
        double cornerNw, double cornerNwY,
        double cornerNe, double cornerNeY,
        double cornerSe, double cornerSeY,
        double cornerSw, double cornerSwY)
    {
        PushClip(rect);
    }

    public abstract object SetAntiAliasSmoothingMode();
    public abstract void ReturnPreviousSmoothingMode(object prevMode);
    public abstract RBrush GetTextureBrush(RImage image, RectangleF dstRect, PointF translateTransformLocation);
    public abstract RGraphicsPath GetGraphicsPath();
    public abstract SizeF MeasureString(string str, RFont font);
    public abstract void MeasureString(string str, RFont font, double maxWidth, out int charFit, out double charFitWidth);
    public abstract void DrawString(string str, RFont font, BColor color, PointF point, SizeF size, bool rtl);
    public abstract void DrawGradientString(string str, RFont font, RectangleF rect, PointF point, SizeF size, bool rtl, BColor[] colors, float[] positions, float angle);
    public abstract void DrawLine(RPen pen, double x1, double y1, double x2, double y2);
    public abstract void DrawRectangle(RPen pen, double x, double y, double width, double height);
    public abstract void DrawRectangle(RBrush brush, double x, double y, double width, double height);
    public abstract void DrawImage(RImage image, RectangleF destRect, RectangleF srcRect);
    public abstract void DrawImage(RImage image, RectangleF destRect);
    public abstract void DrawPath(RPen pen, RGraphicsPath path);
    public abstract void DrawPath(RBrush brush, RGraphicsPath path);
    public abstract void DrawPolygon(RBrush brush, PointF[] points);

    /// <summary>
    /// Hints that the next opacity/blend layer contains only backend-neutral raster operations,
    /// allowing adapters with a custom bitmap backend to keep that compositing group off the fallback backend.
    /// Default implementation ignores the hint.
    /// </summary>
    public virtual void HintNextLayerCanUseRaster(bool canUseRaster) { }

    /// <summary>
    /// Saves the canvas state and begins a new compositing layer with the given opacity (0.0–1.0).
    /// All drawing operations until <see cref="RestoreOpacityLayer"/> are composited as a group
    /// at the specified opacity. Default implementation is a no-op (platform may not support layers).
    /// </summary>
    public virtual void SaveOpacityLayer(float opacity) { }

    /// <summary>
    /// Restores the canvas state from a previous <see cref="SaveOpacityLayer"/> call,
    /// compositing the layer with the specified opacity. Default is a no-op.
    /// </summary>
    public virtual void RestoreOpacityLayer() { }

    /// <summary>
    /// Saves the canvas state and begins a new compositing layer with the given CSS blend mode.
    /// All drawing operations until <see cref="RestoreBlendLayer"/> are composited using
    /// the specified blend mode. Default implementation is a no-op.
    /// </summary>
    public virtual void SaveBlendLayer(string blendMode) { }

    /// <summary>
    /// Restores the canvas state from a previous <see cref="SaveBlendLayer"/> call.
    /// Default is a no-op.
    /// </summary>
    public virtual void RestoreBlendLayer() { }

    /// <summary>
    /// Saves the canvas state and applies a 2D affine transform around the given origin.
    /// The matrix elements are [a, b, c, d, e, f] matching the CSS matrix(a,b,c,d,e,f) function.
    /// Default implementation is a no-op.
    /// </summary>
    public virtual void SaveTransformLayer(float[] matrix, float originX, float originY) { }

    /// <summary>
    /// Restores the canvas state from a previous <see cref="SaveTransformLayer"/> call.
    /// Default is a no-op.
    /// </summary>
    public virtual void RestoreTransformLayer() { }

    /// <summary>Uniform document-root viewport scale about the surface origin; paired with
    /// <see cref="PopViewportScale"/>. Pushed after the device-space viewport clip so content scales
    /// while the clip stays in device pixels. Default no-op.</summary>
    public virtual void PushViewportScale(float scale) { }

    /// <summary>Restores the state saved by <see cref="PushViewportScale"/>. Default no-op.</summary>
    public virtual void PopViewportScale() { }

    /// <summary>
    /// Creates an off-screen gradient image tile.  The returned <see cref="RImage"/>
    /// can be used with <see cref="GetTextureBrush"/> for tiled gradient rendering.
    /// Default implementation returns <c>null</c> (platform may not support off-screen rendering).
    /// </summary>
    /// <param name="width">Tile width in pixels.</param>
    /// <param name="height">Tile height in pixels.</param>
    /// <param name="colors">Gradient color stops.</param>
    /// <param name="positions">Relative positions (0.0–1.0) for each color stop.</param>
    /// <param name="angle">Gradient angle in degrees (0 = top, 90 = right, 180 = bottom).</param>
    public virtual RImage? CreateLinearGradientTile(int width, int height, BColor[] colors, float[] positions, float angle) => null;

    /// <summary>
    /// Creates an off-screen bitmap tile filled with a radial gradient.
    /// <paramref name="centerX"/> and <paramref name="centerY"/> are normalised
    /// fractions (0.0–1.0) of the tile dimensions.
    /// Default implementation returns <c>null</c> (platform may not support off-screen rendering).
    /// </summary>
    public virtual RImage? CreateRadialGradientTile(int width, int height, BColor[] colors, float[] positions, float centerX, float centerY) => null;

    /// <summary>
    /// Creates an off-screen bitmap tile filled with a conic (angular) gradient.
    /// <paramref name="centerX"/> and <paramref name="centerY"/> are normalised
    /// fractions (0.0–1.0) of the tile dimensions; <paramref name="fromAngle"/>
    /// is the starting angle in degrees (clockwise from 12 o'clock) and
    /// <paramref name="positions"/> are fractions of a full turn (0.0–1.0).
    /// Default implementation returns <c>null</c> (platform may not support off-screen rendering).
    /// </summary>
    public virtual RImage? CreateConicGradientTile(int width, int height, BColor[] colors, float[] positions, float centerX, float centerY, float fromAngle) => null;

    public abstract void Dispose();
}
