using System;
using System.Collections.Generic;
using System.Drawing;

namespace Broiler.Graphics;

/// <summary>
/// CPU raster canvas for drawing into <see cref="BBitmap"/> without a native graphics backend.
/// </summary>
public sealed class BCanvas : IDisposable
{
    private readonly BBitmap _rootBitmap;
    private readonly Stack<CanvasState> _stateStack = new();
    private readonly Stack<LayerState> _layerStack = new();
    private readonly List<ClipOperation> _clipOperations = [];

    /// <summary>
    /// Running intersection of the <em>including</em> clip operations, one entry per entry of
    /// <see cref="_clipOperations"/>, in device pixels. The last entry is a bounding box of every
    /// pixel the clip stack can admit.
    /// </summary>
    /// <remarks>
    /// <b>It exists to bound loops, not to decide visibility.</b> <see cref="IsVisible"/> is still
    /// the authority — it handles exclusions and rounded corners, neither of which a bounding box
    /// can express. What the box buys is that a primitive no longer walks pixels the clip is
    /// certain to reject, which on this canvas is most of them: every clipped fill here computes
    /// its loop from its own geometry and then discards, per pixel, whatever the clip excludes.
    /// Kept as a running intersection rather than recomputed so a push or a pop stays O(1).
    /// </remarks>
    private readonly List<RectangleF> _clipBounds = [];

    private PointF _translation;
    private float _scale = 1f;

    public BCanvas(BBitmap bitmap)
    {
        _rootBitmap = bitmap ?? throw new ArgumentNullException(nameof(bitmap));
    }

    public void Save() => _stateStack.Push(new CanvasState(_translation, _scale, _clipOperations.Count));

    public void Restore()
    {
        if (_stateStack.Count == 0)
            return;

        CanvasState state = _stateStack.Pop();
        _translation = state.Translation;
        _scale = state.Scale;

        while (_clipOperations.Count > state.ClipOperationCount)
            PopClip();
    }

    public void Translate(float dx, float dy) =>
        _translation = new PointF(_translation.X + dx, _translation.Y + dy);

    /// <summary>
    /// Composes a uniform scale about the surface origin onto the current transform, so subsequent
    /// draws map <c>point → point * scale + translation</c> (a document-root viewport zoom, e.g. a
    /// pinch-zoom or <c>html { zoom }</c>). Uniform-only: <see cref="Broiler.Graphics.BCanvas"/> is a
    /// translate+uniform-scale rasterizer, not a full affine surface, which is exact for a viewport
    /// zoom. At scale <c>1</c> (the default) every draw path is byte-identical to the translate-only
    /// behaviour. Saved/restored with <see cref="Save"/>/<see cref="Restore"/>.
    /// </summary>
    public void Scale(float scale) => _scale *= scale;

    public void Clear(BColor color) => CurrentTarget.ErasePixels(color);

    public void PushClip(RectangleF rect) => AddClip(ClipOperation.Include(Translate(rect)));

    public void PushClipExclude(RectangleF rect) => AddClip(ClipOperation.Exclude(Translate(rect)));

    public void PushClipRounded(
        RectangleF rect,
        double cornerNw,
        double cornerNwY,
        double cornerNe,
        double cornerNeY,
        double cornerSe,
        double cornerSeY,
        double cornerSw,
        double cornerSwY) =>
        AddClip(ClipOperation.IncludeRounded(
            Translate(rect),
            (float)cornerNw * _scale,
            (float)cornerNwY * _scale,
            (float)cornerNe * _scale,
            (float)cornerNeY * _scale,
            (float)cornerSe * _scale,
            (float)cornerSeY * _scale,
            (float)cornerSw * _scale,
            (float)cornerSwY * _scale));

    public void PushClipExcludeRounded(
        RectangleF rect,
        double cornerNw,
        double cornerNwY,
        double cornerNe,
        double cornerNeY,
        double cornerSe,
        double cornerSeY,
        double cornerSw,
        double cornerSwY) =>
        AddClip(ClipOperation.ExcludeRounded(
            Translate(rect),
            (float)cornerNw * _scale,
            (float)cornerNwY * _scale,
            (float)cornerNe * _scale,
            (float)cornerNeY * _scale,
            (float)cornerSe * _scale,
            (float)cornerSeY * _scale,
            (float)cornerSw * _scale,
            (float)cornerSwY * _scale));

    public void PopClip()
    {
        if (_clipOperations.Count == 0)
            return;

        _clipOperations.RemoveAt(_clipOperations.Count - 1);
        _clipBounds.RemoveAt(_clipBounds.Count - 1);
    }

    /// <summary>
    /// Appends a clip operation and the bounding box the stack admits once it is in effect.
    /// </summary>
    /// <remarks>
    /// An <em>excluding</em> operation carries the running box forward unchanged: it removes pixels
    /// from the admitted set and can never add one, so it cannot narrow a bound that has to stay a
    /// superset of what <see cref="IsVisible"/> accepts. A rounded clip narrows to its bounding
    /// box, which is exactly what <see cref="ClipOperation.Rect"/> already holds.
    /// </remarks>
    private void AddClip(ClipOperation operation)
    {
        RectangleF? previous = _clipBounds.Count > 0 ? _clipBounds[^1] : null;
        RectangleF bounds = operation.IsExclude
            ? previous ?? SurfaceBounds
            : previous is { } current ? RectangleF.Intersect(current, operation.Rect) : operation.Rect;

        _clipOperations.Add(operation);
        _clipBounds.Add(bounds);
    }

    /// <summary>
    /// Stands in for "nothing has narrowed the clip yet" when an excluding operation arrives first,
    /// so the running list stays dense and every entry is a real rectangle.
    /// </summary>
    /// <remarks>
    /// The surface, not an enormous rectangle: the box only ever has to be a superset of the pixels
    /// that can be written, and no pixel outside the surface can be. A sentinel built from
    /// <c>float.MaxValue</c> would be a superset too, and would then be cast to <c>int</c> by
    /// <see cref="NarrowToClip"/> — a conversion that is undefined once the value leaves
    /// <c>int</c>'s range. Every layer buffer is allocated at the surface's size, so this bound
    /// holds whichever target is current.
    /// </remarks>
    private RectangleF SurfaceBounds => new(0f, 0f, _rootBitmap.Width, _rootBitmap.Height);

    /// <summary>
    /// Device-space bounding box of everything the clip stack can admit, or <c>null</c> when
    /// nothing has narrowed it.
    /// </summary>
    private RectangleF? CurrentClipBounds => _clipBounds.Count > 0 ? _clipBounds[^1] : null;

    /// <summary>
    /// Clamps a primitive's device-pixel bounds to the surface and to the rows and columns the
    /// current clip can admit, and reports whether anything is left to draw.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It never drops a pixel <see cref="IsVisible"/> would have kept.</b> A pixel is visible
    /// only if every including clip rectangle contains its centre <c>(x + 0.5, y + 0.5)</c>, so the
    /// leftmost visible column is at least <c>Left - 0.5</c> and <c>floor(Left)</c> is at or below
    /// it; the same argument, mirrored, gives the right edge. The clamp is therefore a conservative
    /// superset of the visible box, and every pixel it removes is one the per-pixel test rejects.
    /// Output is unchanged; the work of walking those pixels is not.
    /// </para>
    /// <para>
    /// Callers pass the geometry's own bounds and read back the narrowed ones. Every primitive
    /// computes each pixel's value from the geometry rather than from the loop bounds, so narrowing
    /// the loop leaves the surviving pixels bit-identical — including <see cref="FillGlyphContours"/>,
    /// whose coverage accumulator is indexed from <c>minX</c> and whose spans are clipped into it.
    /// </para>
    /// </remarks>
    private bool NarrowToClip(ref int minX, ref int minY, ref int maxX, ref int maxY)
    {
        BBitmap target = CurrentTarget;
        minX = Math.Max(0, minX);
        minY = Math.Max(0, minY);
        maxX = Math.Min(target.Width - 1, maxX);
        maxY = Math.Min(target.Height - 1, maxY);

        if (_clipBounds.Count > 0)
        {
            RectangleF bounds = _clipBounds[^1];
            if (bounds.Width <= 0f || bounds.Height <= 0f)
                return false;

            minX = Math.Max(minX, (int)Math.Floor(bounds.Left));
            minY = Math.Max(minY, (int)Math.Floor(bounds.Top));
            maxX = Math.Min(maxX, (int)Math.Ceiling(bounds.Right) - 1);
            maxY = Math.Min(maxY, (int)Math.Ceiling(bounds.Bottom) - 1);
        }

        return minX <= maxX && minY <= maxY;
    }

    /// <summary>
    /// Whether a primitive whose device-space bounds are <paramref name="bounds"/> can put a pixel
    /// anywhere the clip admits. Lets a primitive reject itself before transforming its geometry.
    /// </summary>
    private bool IntersectsClip(RectangleF bounds)
    {
        int minX = (int)Math.Floor(bounds.Left);
        int minY = (int)Math.Floor(bounds.Top);
        int maxX = (int)Math.Ceiling(bounds.Right);
        int maxY = (int)Math.Ceiling(bounds.Bottom);
        return NarrowToClip(ref minX, ref minY, ref maxX, ref maxY);
    }

    /// <summary>
    /// Splits a fill's scanlines into bands across threads, or runs them inline when the fill is
    /// too small to be worth it. Multithreading roadmap item #3; the reasoning is on
    /// <see cref="BRasterParallelism"/>.
    /// </summary>
    /// <remarks>
    /// Takes the clipped pixel bounds rather than a row count because the decision is about area:
    /// a hundred-row fill one pixel wide is not worth a thread and a two-row fill across a 4K
    /// surface may be. <see cref="CurrentTarget"/> is not read per band — the layer a fill draws
    /// into is fixed for the whole fill, exactly as it is in the sequential path.
    /// </remarks>
    private static void ForEachBand(int minY, int maxY, int minX, int maxX, Action<int, int> band) =>
        BRasterParallelism.ForEachBand(
            minY,
            maxY,
            maxX - minX + 1,
            BBitmap.SupportsConcurrentPixelWrites,
            band);

    public void FillRect(RectangleF rect, BColor color)
    {
        RectangleF translated = Translate(rect);
        int minX = (int)Math.Floor(translated.Left);
        int minY = (int)Math.Floor(translated.Top);
        int maxX = (int)Math.Ceiling(translated.Right) - 1;
        int maxY = (int)Math.Ceiling(translated.Bottom) - 1;
        if (!NarrowToClip(ref minX, ref minY, ref maxX, ref maxY))
            return;

        BBitmap target = CurrentTarget;
        ForEachBand(minY, maxY, minX, maxX, (fromY, toY) =>
        {
            for (int y = fromY; y <= toY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (IsVisible(x, y))
                        BlendPixel(target, x, y, color, "normal");
                }
            }
        });
    }

    public void DrawLine(PointF start, PointF end, BColor color, float strokeWidth = 1f)
    {
        PointF p1 = Translate(start);
        PointF p2 = Translate(end);
        float radius = Math.Max(0.5f, strokeWidth * _scale / 2f);

        int minX = (int)Math.Floor(Math.Min(p1.X, p2.X) - radius);
        int minY = (int)Math.Floor(Math.Min(p1.Y, p2.Y) - radius);
        int maxX = (int)Math.Ceiling(Math.Max(p1.X, p2.X) + radius);
        int maxY = (int)Math.Ceiling(Math.Max(p1.Y, p2.Y) + radius);
        if (!NarrowToClip(ref minX, ref minY, ref maxX, ref maxY))
            return;

        BBitmap target = CurrentTarget;
        ForEachBand(minY, maxY, minX, maxX, (fromY, toY) =>
        {
            for (int y = fromY; y <= toY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (!IsVisible(x, y))
                        continue;

                    float distance = DistanceToSegment(x + 0.5f, y + 0.5f, p1, p2);
                    if (distance <= radius)
                        BlendPixel(target, x, y, color, "normal");
                }
            }
        });
    }

    public void DrawRectangleStroke(RectangleF rect, BColor color, float strokeWidth = 1f)
    {
        strokeWidth = Math.Max(1f, strokeWidth);
        FillRect(new RectangleF(rect.X, rect.Y, rect.Width, strokeWidth), color);
        FillRect(new RectangleF(rect.X, rect.Bottom - strokeWidth, rect.Width, strokeWidth), color);
        FillRect(new RectangleF(rect.X, rect.Y, strokeWidth, rect.Height), color);
        FillRect(new RectangleF(rect.Right - strokeWidth, rect.Y, strokeWidth, rect.Height), color);
    }

    public void FillRoundedRect(RectangleF rect, BColor color, float radiusX, float radiusY)
    {
        if (rect.Width <= 0 || rect.Height <= 0 || color.A == 0)
            return;

        radiusX = Math.Clamp(radiusX, 0f, rect.Width / 2f);
        radiusY = Math.Clamp(radiusY, 0f, rect.Height / 2f);
        if (radiusX <= 0 || radiusY <= 0)
        {
            FillRect(rect, color);
            return;
        }

        Save();
        PushClipRounded(rect, radiusX, radiusY, radiusX, radiusY, radiusX, radiusY, radiusX, radiusY);
        FillRect(rect, color);
        Restore();
    }

    public void DrawRoundedRectangleStroke(RectangleF rect, BColor color, float radiusX, float radiusY, float strokeWidth = 1f)
    {
        if (rect.Width <= 0 || rect.Height <= 0 || color.A == 0 || strokeWidth <= 0)
            return;

        strokeWidth = Math.Max(1f, strokeWidth);
        radiusX = Math.Clamp(radiusX, 0f, rect.Width / 2f);
        radiusY = Math.Clamp(radiusY, 0f, rect.Height / 2f);
        if (radiusX <= 0 || radiusY <= 0)
        {
            DrawRectangleStroke(rect, color, strokeWidth);
            return;
        }

        Save();
        PushClipRounded(rect, radiusX, radiusY, radiusX, radiusY, radiusX, radiusY, radiusX, radiusY);
        RectangleF inner = Inset(rect, strokeWidth);
        if (inner.Width > 0 && inner.Height > 0)
        {
            float innerRadiusX = Math.Max(0f, radiusX - strokeWidth);
            float innerRadiusY = Math.Max(0f, radiusY - strokeWidth);
            PushClipExcludeRounded(inner, innerRadiusX, innerRadiusY, innerRadiusX, innerRadiusY, innerRadiusX, innerRadiusY, innerRadiusX, innerRadiusY);
        }

        FillRect(rect, color);
        Restore();
    }

    public void FillPolygon(PointF[] points, BColor color)
    {
        if (points == null || points.Length < 3)
            return;

        PointF[] translated = new PointF[points.Length];
        float minX = float.PositiveInfinity;
        float minY = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float maxY = float.NegativeInfinity;

        for (int i = 0; i < points.Length; i++)
        {
            PointF point = Translate(points[i]);
            translated[i] = point;
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
        }

        int startX = (int)Math.Floor(minX);
        int startY = (int)Math.Floor(minY);
        int endX = (int)Math.Ceiling(maxX);
        int endY = (int)Math.Ceiling(maxY);
        if (!NarrowToClip(ref startX, ref startY, ref endX, ref endY))
            return;

        BBitmap target = CurrentTarget;
        ForEachBand(startY, endY, startX, endX, (fromY, toY) =>
        {
            for (int y = fromY; y <= toY; y++)
            {
                for (int x = startX; x <= endX; x++)
                {
                    if (IsVisible(x, y) && ContainsPolygonPoint(translated, x + 0.5f, y + 0.5f))
                        BlendPixel(target, x, y, color, "normal");
                }
            }
        });
    }

    public void FillGlyphContours(IReadOnlyList<PointF[]> contours, BColor color)
    {
        if (contours == null || contours.Count == 0 || color.A == 0)
            return;

        // Reject the glyph on its bounding box before transforming and copying its points. Text is
        // the one primitive a document issues thousands of times, and a document is usually taller
        // than the surface it is being drawn into — so the allocation below is worth skipping
        // rather than doing and then discarding. The box goes through the same mapping as the
        // points do, so a glyph that survives it is measured no differently than before.
        if (!IntersectsClip(Translate(BoundingBox(contours))))
            return;

        float minXf = float.PositiveInfinity;
        float minYf = float.PositiveInfinity;
        float maxXf = float.NegativeInfinity;
        float maxYf = float.NegativeInfinity;
        var deviceContours = new PointF[contours.Count][];

        for (int contourIndex = 0; contourIndex < contours.Count; contourIndex++)
        {
            PointF[] source = contours[contourIndex];
            var destination = new PointF[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                PointF point = Translate(source[i]);
                destination[i] = point;
                minXf = Math.Min(minXf, point.X);
                minYf = Math.Min(minYf, point.Y);
                maxXf = Math.Max(maxXf, point.X);
                maxYf = Math.Max(maxYf, point.Y);
            }

            deviceContours[contourIndex] = destination;
        }

        if (float.IsInfinity(minXf))
            return;

        int minX = (int)Math.Floor(minXf);
        int minY = (int)Math.Floor(minYf);
        int maxX = (int)Math.Ceiling(maxXf);
        int maxY = (int)Math.Ceiling(maxYf);
        if (!NarrowToClip(ref minX, ref minY, ref maxX, ref maxY))
            return;

        const int subSamples = 4;
        int width = maxX - minX + 1;
        BBitmap target = CurrentTarget;

        // The coverage accumulator and the crossing list are per band, not per canvas: they are
        // the only mutable state a scanline carries, and giving each band its own is what lets the
        // bands run at once. A glyph is normally far below the parallel threshold and takes the
        // inline path with exactly one band — this matters for the large fills (headline text, SVG
        // outlines) that are not.
        ForEachBand(minY, maxY, minX, maxX, (fromY, toY) =>
        {
            var coverage = new float[width];
            var crossings = new List<(float X, int Direction)>(16);

            for (int y = fromY; y <= toY; y++)
            {
                Array.Clear(coverage, 0, width);

                for (int sample = 0; sample < subSamples; sample++)
                {
                    float sampleY = y + (sample + 0.5f) / subSamples;
                    crossings.Clear();

                    foreach (PointF[] polygon in deviceContours)
                    {
                        int count = polygon.Length;
                        for (int i = 0; i < count; i++)
                        {
                            PointF p0 = polygon[i];
                            PointF p1 = polygon[(i + 1) % count];
                            if (p0.Y == p1.Y)
                                continue;

                            float low = Math.Min(p0.Y, p1.Y);
                            float high = Math.Max(p0.Y, p1.Y);
                            if (sampleY < low || sampleY >= high)
                                continue;

                            float t = (sampleY - p0.Y) / (p1.Y - p0.Y);
                            float xCross = p0.X + (t * (p1.X - p0.X));
                            crossings.Add((xCross, p1.Y > p0.Y ? 1 : -1));
                        }
                    }

                    if (crossings.Count < 2)
                        continue;

                    crossings.Sort(static (left, right) => left.X.CompareTo(right.X));

                    int winding = 0;
                    for (int i = 0; i < crossings.Count - 1; i++)
                    {
                        winding += crossings[i].Direction;
                        if (winding != 0)
                            AccumulateGlyphSpan(coverage, minX, crossings[i].X, crossings[i + 1].X, 1f / subSamples);
                    }
                }

                for (int i = 0; i < width; i++)
                {
                    float cov = Math.Clamp(coverage[i], 0f, 1f);
                    if (cov <= 0f)
                        continue;

                    int x = minX + i;
                    if (!IsVisible(x, y))
                        continue;

                    byte alpha = (byte)Math.Clamp((int)Math.Round(color.A * cov), 0, 255);
                    if (alpha != 0)
                        BlendPixel(target, x, y, new BColor(color.R, color.G, color.B, alpha), "normal");
                }
            }
        });
    }

    /// <summary>User-space bounding box of a set of contours, empty when they hold no points.</summary>
    private static RectangleF BoundingBox(IReadOnlyList<PointF[]> contours)
    {
        float minX = float.PositiveInfinity;
        float minY = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float maxY = float.NegativeInfinity;

        for (int contourIndex = 0; contourIndex < contours.Count; contourIndex++)
        {
            PointF[] points = contours[contourIndex];
            for (int i = 0; i < points.Length; i++)
            {
                PointF point = points[i];
                minX = Math.Min(minX, point.X);
                minY = Math.Min(minY, point.Y);
                maxX = Math.Max(maxX, point.X);
                maxY = Math.Max(maxY, point.Y);
            }
        }

        return float.IsInfinity(minX)
            ? RectangleF.Empty
            : new RectangleF(minX, minY, maxX - minX, maxY - minY);
    }

    public void DrawBitmap(BBitmap source, RectangleF destRect, RectangleF srcRect)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (destRect.Width <= 0 || destRect.Height <= 0 || srcRect.Width <= 0 || srcRect.Height <= 0)
            return;

        RectangleF translatedDest = Translate(destRect);
        int startX = (int)Math.Floor(translatedDest.Left);
        int startY = (int)Math.Floor(translatedDest.Top);
        int endX = (int)Math.Ceiling(translatedDest.Right) - 1;
        int endY = (int)Math.Ceiling(translatedDest.Bottom) - 1;
        if (!NarrowToClip(ref startX, ref startY, ref endX, ref endY))
            return;

        BBitmap target = CurrentTarget;
        ForEachBand(startY, endY, startX, endX, (fromY, toY) =>
        {
            for (int y = fromY; y <= toY; y++)
            {
                for (int x = startX; x <= endX; x++)
                {
                    if (!IsVisible(x, y))
                        continue;

                    float normalizedX = ((x + 0.5f) - translatedDest.Left) / translatedDest.Width;
                    float normalizedY = ((y + 0.5f) - translatedDest.Top) / translatedDest.Height;
                    if (normalizedX < 0f || normalizedX >= 1f || normalizedY < 0f || normalizedY >= 1f)
                        continue;

                    int srcX = Math.Clamp((int)Math.Floor(srcRect.Left + (normalizedX * srcRect.Width)), 0, source.Width - 1);
                    int srcY = Math.Clamp((int)Math.Floor(srcRect.Top + (normalizedY * srcRect.Height)), 0, source.Height - 1);
                    BlendPixel(target, x, y, source.GetPixel(srcX, srcY), "normal");
                }
            }
        });
    }

    public void DrawPathStroke(IReadOnlyList<PointF> points, BColor color, float strokeWidth = 1f)
    {
        if (points == null || points.Count < 2)
            return;

        for (int i = 1; i < points.Count; i++)
            DrawLine(points[i - 1], points[i], color, strokeWidth);
    }

    public void FillRectTiled(BBitmap source, RectangleF destRect, RectangleF srcRect, PointF tileOrigin)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (destRect.Width <= 0 || destRect.Height <= 0 || srcRect.Width <= 0 || srcRect.Height <= 0)
            return;

        RectangleF translatedDest = Translate(destRect);
        PointF translatedOrigin = Translate(tileOrigin);
        int minX = (int)Math.Floor(translatedDest.Left);
        int minY = (int)Math.Floor(translatedDest.Top);
        int maxX = (int)Math.Ceiling(translatedDest.Right) - 1;
        int maxY = (int)Math.Ceiling(translatedDest.Bottom) - 1;
        if (!NarrowToClip(ref minX, ref minY, ref maxX, ref maxY))
            return;

        BBitmap target = CurrentTarget;
        ForEachBand(minY, maxY, minX, maxX, (fromY, toY) =>
        {
            for (int y = fromY; y <= toY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (!IsVisible(x, y))
                        continue;

                    float sampleX = x + 0.5f;
                    float sampleY = y + 0.5f;
                    int srcX = Math.Clamp(
                        (int)Math.Floor(srcRect.Left + PositiveModulo(sampleX - translatedOrigin.X, srcRect.Width)),
                        0,
                        source.Width - 1);
                    int srcY = Math.Clamp(
                        (int)Math.Floor(srcRect.Top + PositiveModulo(sampleY - translatedOrigin.Y, srcRect.Height)),
                        0,
                        source.Height - 1);
                    BlendPixel(target, x, y, source.GetPixel(srcX, srcY), "normal");
                }
            }
        });
    }

    public void FillLinearGradientRect(RectangleF rect, IReadOnlyList<BColor> colors, IReadOnlyList<float>? positions, float angle)
    {
        if (colors == null || colors.Count == 0 || rect.Width <= 0 || rect.Height <= 0)
            return;

        if (colors.Count == 1)
        {
            FillRect(rect, colors[0]);
            return;
        }

        RectangleF translatedRect = Translate(rect);
        float[] normalizedPositions = NormalizeGradientPositions(colors.Count, positions);
        (PointF startPoint, PointF endPoint) = GetGradientEndpoints(translatedRect, angle);
        float gradientX = endPoint.X - startPoint.X;
        float gradientY = endPoint.Y - startPoint.Y;
        float gradientLengthSquared = (gradientX * gradientX) + (gradientY * gradientY);

        if (gradientLengthSquared <= 0f)
        {
            FillRect(rect, colors[^1]);
            return;
        }

        int minX = (int)Math.Floor(translatedRect.Left);
        int minY = (int)Math.Floor(translatedRect.Top);
        int maxX = (int)Math.Ceiling(translatedRect.Right) - 1;
        int maxY = (int)Math.Ceiling(translatedRect.Bottom) - 1;
        if (!NarrowToClip(ref minX, ref minY, ref maxX, ref maxY))
            return;

        BBitmap target = CurrentTarget;
        ForEachBand(minY, maxY, minX, maxX, (fromY, toY) =>
        {
            for (int y = fromY; y <= toY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (!IsVisible(x, y))
                        continue;

                    float sampleX = x + 0.5f;
                    float sampleY = y + 0.5f;
                    float t = (((sampleX - startPoint.X) * gradientX) + ((sampleY - startPoint.Y) * gradientY)) / gradientLengthSquared;
                    BlendPixel(target, x, y, SampleGradientColor(colors, normalizedPositions, Math.Clamp(t, 0f, 1f)), "normal");
                }
            }
        });
    }

    public void FillRadialGradientRect(RectangleF rect, IReadOnlyList<BColor> colors, IReadOnlyList<float>? positions, float centerX, float centerY)
    {
        if (colors == null || colors.Count == 0 || rect.Width <= 0 || rect.Height <= 0)
            return;

        if (colors.Count == 1)
        {
            FillRect(rect, colors[0]);
            return;
        }

        RectangleF translatedRect = Translate(rect);
        float[] normalizedPositions = NormalizeGradientPositions(colors.Count, positions);

        float cx = translatedRect.Left + (centerX * translatedRect.Width);
        float cy = translatedRect.Top + (centerY * translatedRect.Height);
        float rx = Math.Max(Math.Abs(cx - translatedRect.Left), Math.Abs(cx - translatedRect.Right));
        float ry = Math.Max(Math.Abs(cy - translatedRect.Top), Math.Abs(cy - translatedRect.Bottom));
        if (rx <= 0 || ry <= 0)
            return;

        int minX = (int)Math.Floor(translatedRect.Left);
        int minY = (int)Math.Floor(translatedRect.Top);
        int maxX = (int)Math.Ceiling(translatedRect.Right) - 1;
        int maxY = (int)Math.Ceiling(translatedRect.Bottom) - 1;
        if (!NarrowToClip(ref minX, ref minY, ref maxX, ref maxY))
            return;

        BBitmap target = CurrentTarget;
        ForEachBand(minY, maxY, minX, maxX, (fromY, toY) =>
        {
            for (int y = fromY; y <= toY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (!IsVisible(x, y))
                        continue;

                    float dx = (x + 0.5f - cx) / rx;
                    float dy = (y + 0.5f - cy) / ry;
                    float t = Math.Clamp((float)Math.Sqrt((dx * dx) + (dy * dy)), 0f, 1f);
                    BlendPixel(target, x, y, SampleGradientColor(colors, normalizedPositions, t), "normal");
                }
            }
        });
    }

    public void FillConicGradientRect(
        RectangleF rect,
        IReadOnlyList<BColor> colors,
        IReadOnlyList<float>? positions,
        float centerX,
        float centerY,
        float fromAngleDeg)
    {
        if (colors == null || colors.Count == 0 || rect.Width <= 0 || rect.Height <= 0)
            return;

        if (colors.Count == 1)
        {
            FillRect(rect, colors[0]);
            return;
        }

        RectangleF translatedRect = Translate(rect);
        float[] normalizedPositions = NormalizeGradientPositions(colors.Count, positions);
        float cx = translatedRect.Left + (centerX * translatedRect.Width);
        float cy = translatedRect.Top + (centerY * translatedRect.Height);

        int minX = (int)Math.Floor(translatedRect.Left);
        int minY = (int)Math.Floor(translatedRect.Top);
        int maxX = (int)Math.Ceiling(translatedRect.Right) - 1;
        int maxY = (int)Math.Ceiling(translatedRect.Bottom) - 1;
        if (!NarrowToClip(ref minX, ref minY, ref maxX, ref maxY))
            return;

        BBitmap target = CurrentTarget;
        ForEachBand(minY, maxY, minX, maxX, (fromY, toY) =>
        {
            for (int y = fromY; y <= toY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (!IsVisible(x, y))
                        continue;

                    float dx = x + 0.5f - cx;
                    float dy = y + 0.5f - cy;
                    float angleDeg = (float)(Math.Atan2(dx, -dy) * 180.0 / Math.PI);
                    float t = PositiveModulo(angleDeg - fromAngleDeg, 360f) / 360f;
                    BlendPixel(target, x, y, SampleGradientColor(colors, normalizedPositions, t), "normal");
                }
            }
        });
    }

    public void SaveOpacityLayer(float opacity) =>
        _layerStack.Push(new LayerState(new BBitmap(_rootBitmap.Width, _rootBitmap.Height), opacity, "normal", CurrentClipBounds));

    public void RestoreOpacityLayer()
    {
        if (_layerStack.Count > 0)
            CompositeLayer(_layerStack.Pop());
    }

    public void SaveBlendLayer(string blendMode) =>
        _layerStack.Push(new LayerState(new BBitmap(_rootBitmap.Width, _rootBitmap.Height), 1f, blendMode ?? "normal", CurrentClipBounds));

    public void RestoreBlendLayer()
    {
        if (_layerStack.Count > 0)
            CompositeLayer(_layerStack.Pop());
    }

    public void Dispose()
    {
        while (_layerStack.Count > 0)
            _layerStack.Pop().Bitmap.Dispose();
    }

    private BBitmap CurrentTarget => _layerStack.Count > 0 ? _layerStack.Peek().Bitmap : _rootBitmap;

    // Map a layout-space rect/point to device space: point * _scale + _translation. At _scale == 1
    // this is the original translate-only mapping (rect size and position unchanged but for the pan).
    private RectangleF Translate(RectangleF rect) =>
        new(rect.X * _scale + _translation.X, rect.Y * _scale + _translation.Y, rect.Width * _scale, rect.Height * _scale);

    private PointF Translate(PointF point) =>
        new(point.X * _scale + _translation.X, point.Y * _scale + _translation.Y);

    private static RectangleF Inset(RectangleF rect, float amount) =>
        new(rect.X + amount, rect.Y + amount, Math.Max(0, rect.Width - amount * 2), Math.Max(0, rect.Height - amount * 2));

    private bool IsVisible(int x, int y)
    {
        float sampleX = x + 0.5f;
        float sampleY = y + 0.5f;

        foreach (ClipOperation operation in _clipOperations)
        {
            bool contains = operation.Contains(sampleX, sampleY);
            if (operation.IsExclude)
            {
                if (contains)
                    return false;
            }
            else if (!contains)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Blends a layer's buffer back into the target underneath it, over the box the layer's clip
    /// could have let it write rather than over the whole surface.
    /// </summary>
    /// <remarks>
    /// The bound is the clip in force when the layer was <em>pushed</em>, recorded then rather than
    /// read now: the layer's own draws may have pushed and popped clips of their own, so the stack
    /// at restore time says nothing about where the layer's pixels went. Outside that box every
    /// source pixel is transparent — nothing could have written one — and the loop already skips
    /// transparent sources, so this removes only iterations, never a blend.
    /// </remarks>
    private void CompositeLayer(LayerState layer)
    {
        BBitmap destination = CurrentTarget;
        int minX = 0, minY = 0, maxX = destination.Width - 1, maxY = destination.Height - 1;
        if (layer.ContentBounds is { } bounds)
        {
            minX = Math.Max(minX, (int)Math.Floor(bounds.Left));
            minY = Math.Max(minY, (int)Math.Floor(bounds.Top));
            maxX = Math.Min(maxX, (int)Math.Ceiling(bounds.Right) - 1);
            maxY = Math.Min(maxY, (int)Math.Ceiling(bounds.Bottom) - 1);
        }

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                BColor source = layer.Bitmap.GetPixel(x, y);
                if (source.A == 0)
                    continue;

                if (layer.Opacity < 1f)
                    source = ApplyOpacity(source, layer.Opacity);

                BlendPixel(destination, x, y, source, layer.BlendMode);
            }
        }

        layer.Bitmap.Dispose();
    }

    private static void AccumulateGlyphSpan(float[] coverage, int minX, float spanStart, float spanEnd, float weight)
    {
        if (spanEnd <= spanStart)
            return;

        int width = coverage.Length;
        int ixStart = Math.Max(0, (int)Math.Floor(spanStart) - minX);
        int ixEnd = Math.Min(width, (int)Math.Ceiling(spanEnd) - minX);

        for (int ix = ixStart; ix < ixEnd; ix++)
        {
            float pixelLeft = minX + ix;
            float pixelRight = pixelLeft + 1f;
            float covLeft = Math.Max(spanStart, pixelLeft);
            float covRight = Math.Min(spanEnd, pixelRight);
            float fraction = covRight - covLeft;
            if (fraction > 0f)
                coverage[ix] += fraction * weight;
        }
    }

    private static BColor ApplyOpacity(BColor color, float opacity)
    {
        opacity = Math.Clamp(opacity, 0f, 1f);
        byte alpha = (byte)Math.Clamp((int)Math.Round(color.A * opacity), 0, 255);
        return new BColor(color.R, color.G, color.B, alpha);
    }

    private static void BlendPixel(BBitmap bitmap, int x, int y, BColor source, string blendMode)
    {
        if (source.A == 0)
            return;

        BColor destination = bitmap.GetPixel(x, y);
        BColor blendedSource = ApplyBlendMode(source, destination, blendMode);
        bitmap.WritePixelUnchecked(x, y, CompositeSourceOver(blendedSource, destination));
    }

    private static BColor ApplyBlendMode(BColor source, BColor destination, string blendMode)
    {
        if (string.Equals(blendMode, "multiply", StringComparison.OrdinalIgnoreCase))
        {
            return new BColor(
                (byte)((source.R * destination.R + 127) / 255),
                (byte)((source.G * destination.G + 127) / 255),
                (byte)((source.B * destination.B + 127) / 255),
                source.A);
        }

        if (string.Equals(blendMode, "screen", StringComparison.OrdinalIgnoreCase))
        {
            return new BColor(
                (byte)(255 - (((255 - source.R) * (255 - destination.R) + 127) / 255)),
                (byte)(255 - (((255 - source.G) * (255 - destination.G) + 127) / 255)),
                (byte)(255 - (((255 - source.B) * (255 - destination.B) + 127) / 255)),
                source.A);
        }

        if (string.Equals(blendMode, "darken", StringComparison.OrdinalIgnoreCase))
            return new BColor(Math.Min(source.R, destination.R), Math.Min(source.G, destination.G), Math.Min(source.B, destination.B), source.A);

        if (string.Equals(blendMode, "lighten", StringComparison.OrdinalIgnoreCase))
            return new BColor(Math.Max(source.R, destination.R), Math.Max(source.G, destination.G), Math.Max(source.B, destination.B), source.A);

        if (string.Equals(blendMode, "overlay", StringComparison.OrdinalIgnoreCase))
            return new BColor(OverlayChannel(source.R, destination.R), OverlayChannel(source.G, destination.G), OverlayChannel(source.B, destination.B), source.A);

        if (string.Equals(blendMode, "difference", StringComparison.OrdinalIgnoreCase))
            return new BColor((byte)Math.Abs(source.R - destination.R), (byte)Math.Abs(source.G - destination.G), (byte)Math.Abs(source.B - destination.B), source.A);

        if (string.Equals(blendMode, "plus-lighter", StringComparison.OrdinalIgnoreCase))
            return new BColor(AdditiveClampChannel(source.R, destination.R), AdditiveClampChannel(source.G, destination.G), AdditiveClampChannel(source.B, destination.B), source.A);

        return source;
    }

    private static float[] NormalizeGradientPositions(int colorCount, IReadOnlyList<float>? positions)
    {
        var normalized = new float[colorCount];
        if (positions == null || positions.Count != colorCount)
        {
            if (colorCount == 1)
            {
                normalized[0] = 0f;
                return normalized;
            }

            for (int i = 0; i < colorCount; i++)
                normalized[i] = (float)i / (colorCount - 1);

            return normalized;
        }

        normalized[0] = Math.Clamp(positions[0], 0f, 1f);
        for (int i = 1; i < colorCount; i++)
            normalized[i] = Math.Max(normalized[i - 1], Math.Clamp(positions[i], 0f, 1f));

        return normalized;
    }

    private static (PointF StartPoint, PointF EndPoint) GetGradientEndpoints(RectangleF rect, float angle)
    {
        double radians = angle * Math.PI / 180.0;
        float cx = rect.X + (rect.Width / 2f);
        float cy = rect.Y + (rect.Height / 2f);
        float halfDiag = Math.Max(rect.Width, rect.Height) / 2f;
        float sin = (float)Math.Sin(radians);
        float cos = (float)Math.Cos(radians);
        return (
            new PointF(cx - (sin * halfDiag), cy + (cos * halfDiag)),
            new PointF(cx + (sin * halfDiag), cy - (cos * halfDiag)));
    }

    private static BColor SampleGradientColor(IReadOnlyList<BColor> colors, IReadOnlyList<float> positions, float t)
    {
        if (t <= positions[0])
            return colors[0];

        for (int i = 1; i < colors.Count; i++)
        {
            if (t > positions[i])
                continue;

            float start = positions[i - 1];
            float end = positions[i];
            if (end <= start)
                return colors[i];

            float localT = (t - start) / (end - start);
            return Lerp(colors[i - 1], colors[i], localT);
        }

        return colors[^1];
    }

    private static BColor Lerp(BColor start, BColor end, float t) =>
        new(
            LerpChannel(start.R, end.R, t),
            LerpChannel(start.G, end.G, t),
            LerpChannel(start.B, end.B, t),
            LerpChannel(start.A, end.A, t));

    private static byte LerpChannel(byte start, byte end, float t) =>
        (byte)Math.Clamp((int)Math.Round(start + ((end - start) * t)), 0, 255);

    private static float PositiveModulo(float value, float modulus)
    {
        float result = value % modulus;
        if (result < 0)
            result += modulus;
        return result;
    }

    private static BColor CompositeSourceOver(BColor source, BColor destination)
    {
        float srcA = source.A / 255f;
        float dstA = destination.A / 255f;
        float outA = srcA + (dstA * (1f - srcA));

        if (outA <= 0f)
            return BColor.Transparent;

        byte r = CompositeChannel(source.R, destination.R, srcA, dstA, outA);
        byte g = CompositeChannel(source.G, destination.G, srcA, dstA, outA);
        byte b = CompositeChannel(source.B, destination.B, srcA, dstA, outA);
        byte a = (byte)Math.Clamp((int)Math.Round(outA * 255f), 0, 255);

        return new BColor(r, g, b, a);
    }

    private static byte CompositeChannel(byte source, byte destination, float srcA, float dstA, float outA)
    {
        float value = ((source * srcA) + (destination * dstA * (1f - srcA))) / outA;
        return (byte)Math.Clamp((int)Math.Round(value), 0, 255);
    }

    private static byte OverlayChannel(byte source, byte destination)
    {
        if (destination < 128)
            return (byte)Math.Clamp((2 * source * destination + 127) / 255, 0, 255);

        return (byte)Math.Clamp(255 - ((2 * (255 - source) * (255 - destination) + 127) / 255), 0, 255);
    }

    private static byte AdditiveClampChannel(byte source, byte destination) =>
        (byte)Math.Min(255, source + destination);

    private static float DistanceToSegment(float px, float py, PointF start, PointF end)
    {
        float dx = end.X - start.X;
        float dy = end.Y - start.Y;

        if (Math.Abs(dx) < float.Epsilon && Math.Abs(dy) < float.Epsilon)
            return Distance(px, py, start.X, start.Y);

        float t = ((px - start.X) * dx + ((py - start.Y) * dy)) / ((dx * dx) + (dy * dy));
        t = Math.Clamp(t, 0f, 1f);

        float nearestX = start.X + (t * dx);
        float nearestY = start.Y + (t * dy);
        return Distance(px, py, nearestX, nearestY);
    }

    private static float Distance(float x1, float y1, float x2, float y2)
    {
        float dx = x2 - x1;
        float dy = y2 - y1;
        return (float)Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static bool ContainsPolygonPoint(PointF[] polygon, float x, float y)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            PointF pi = polygon[i];
            PointF pj = polygon[j];
            bool intersects = ((pi.Y > y) != (pj.Y > y))
                && (x < (((pj.X - pi.X) * (y - pi.Y)) / ((pj.Y - pi.Y) + float.Epsilon)) + pi.X);
            if (intersects)
                inside = !inside;
        }

        return inside;
    }

    private readonly record struct CanvasState(PointF Translation, float Scale, int ClipOperationCount);

    /// <param name="ContentBounds">
    /// Device-space box the clip admitted when the layer was pushed, or <c>null</c> when nothing
    /// had narrowed it. See <see cref="CompositeLayer"/>.
    /// </param>
    private sealed record LayerState(BBitmap Bitmap, float Opacity, string BlendMode, RectangleF? ContentBounds);

    private readonly record struct ClipOperation(
        RectangleF Rect,
        bool IsExclude,
        bool IsRounded,
        float CornerNw,
        float CornerNwY,
        float CornerNe,
        float CornerNeY,
        float CornerSe,
        float CornerSeY,
        float CornerSw,
        float CornerSwY)
    {
        public static ClipOperation Include(RectangleF rect) => new(rect, false, false, 0, 0, 0, 0, 0, 0, 0, 0);

        public static ClipOperation Exclude(RectangleF rect) => new(rect, true, false, 0, 0, 0, 0, 0, 0, 0, 0);

        public static ClipOperation IncludeRounded(
            RectangleF rect,
            float cornerNw,
            float cornerNwY,
            float cornerNe,
            float cornerNeY,
            float cornerSe,
            float cornerSeY,
            float cornerSw,
            float cornerSwY) =>
            new(rect, false, true, cornerNw, cornerNwY, cornerNe, cornerNeY, cornerSe, cornerSeY, cornerSw, cornerSwY);

        public static ClipOperation ExcludeRounded(
            RectangleF rect,
            float cornerNw,
            float cornerNwY,
            float cornerNe,
            float cornerNeY,
            float cornerSe,
            float cornerSeY,
            float cornerSw,
            float cornerSwY) =>
            new(rect, true, true, cornerNw, cornerNwY, cornerNe, cornerNeY, cornerSe, cornerSeY, cornerSw, cornerSwY);

        public bool Contains(float x, float y)
        {
            if (!Rect.Contains(x, y))
                return false;

            return !IsRounded || ContainsRounded(x, y);
        }

        /// <summary>
        /// Whether a point inside the clip's bounding rect is inside its rounded shape.
        /// <para>
        /// Only the four corner boxes are curved — a corner box spanning that corner's two radii —
        /// and a point inside one is inside the shape only if it is inside that corner's ellipse.
        /// Everything else within the rect is simply inside. A zero radius makes its corner box
        /// empty, so that corner stays square with no special case.
        /// </para>
        /// <para>
        /// This replaces a test that asked whether the point lay in a horizontal or vertical band
        /// between opposing radii. Those bands span the whole box as soon as the opposing corner is
        /// square: with only a top-left radius set, the "between the bottom corners" band covered
        /// every row, so the shape reported itself as the full rectangle and a single rounded corner
        /// clipped nothing. It only clipped correctly when all four corners were rounded.
        /// </para>
        /// </summary>
        private bool ContainsRounded(float x, float y)
        {
            if (x < Rect.Left + CornerNw && y < Rect.Top + CornerNwY)
                return InEllipse(x, y, Rect.Left + CornerNw, Rect.Top + CornerNwY, CornerNw, CornerNwY);

            if (x > Rect.Right - CornerNe && y < Rect.Top + CornerNeY)
                return InEllipse(x, y, Rect.Right - CornerNe, Rect.Top + CornerNeY, CornerNe, CornerNeY);

            if (x > Rect.Right - CornerSe && y > Rect.Bottom - CornerSeY)
                return InEllipse(x, y, Rect.Right - CornerSe, Rect.Bottom - CornerSeY, CornerSe, CornerSeY);

            if (x < Rect.Left + CornerSw && y > Rect.Bottom - CornerSwY)
                return InEllipse(x, y, Rect.Left + CornerSw, Rect.Bottom - CornerSwY, CornerSw, CornerSwY);

            return true;
        }

        private static bool InEllipse(float x, float y, float centerX, float centerY, float radiusX, float radiusY)
        {
            float dx = (x - centerX) / radiusX;
            float dy = (y - centerY) / radiusY;
            return ((dx * dx) + (dy * dy)) <= 1f;
        }
    }
}
