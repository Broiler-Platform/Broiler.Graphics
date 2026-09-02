using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;

namespace Broiler.Graphics;

/// <summary>
/// Platform-neutral renderer that replays <see cref="BRenderList"/> commands into
/// <see cref="BImageSurface"/> / <see cref="BBitmap"/> targets.
/// <para>
/// <b>Thread-safe.</b> One renderer may serve several threads at once: the image
/// table is concurrent, handle allocation is interlocked, and the transform state
/// a replay walks is created per <see cref="Render"/> call rather than held on the
/// instance. Two threads must still not render into the same
/// <see cref="IBroilerSurface"/>, which owns the pixels they would both write.
/// </para>
/// </summary>
public sealed class BImageRenderer : IBroilerRenderer
{
    // Concurrent, and interlocked handle allocation with it. Both are on the
    // render path through a renderer that hosts share, so `++_nextImageId` was a
    // read-modify-write that could hand two images the same handle — the second
    // CreateImage then overwrote the first's bitmap and the first drew the wrong
    // picture, with nothing thrown and nothing logged.
    private readonly ConcurrentDictionary<ulong, BBitmap> _images = new();
    private long _nextImageId;
    private volatile bool _disposed;

    /// <summary>
    /// Transform state for one replay. Held per call rather than per instance so
    /// that two <see cref="Render"/> calls in flight cannot pop each other's
    /// transforms — the stack is the only thing a replay mutates, and it is
    /// meaningless outside the list being replayed.
    /// </summary>
    private sealed class ReplayState(double dpiScale)
    {
        public readonly Stack<BMatrix3x2> TransformStack = new();
        public BMatrix3x2 CurrentTransform = BMatrix3x2.Identity;
        public readonly BMatrix3x2 PixelScaleTransform = BMatrix3x2.Scale(dpiScale, dpiScale);

        public BMatrix3x2 Effective => CurrentTransform * PixelScaleTransform;
    }

    public IBroilerSurface CreateSurface(BSurfaceDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new BImageSurface(descriptor);
    }

    public BImageHandle CreateImage(ReadOnlySpan<byte> encodedImage)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return CreateImage(MediaImageBridge.Decode(encodedImage));
    }

    public BImageHandle CreateImage(BPixelBuffer pixels)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(pixels);

        ulong id = (ulong)Interlocked.Increment(ref _nextImageId);
        _images[id] = new BBitmap(pixels.Width, pixels.Height, (byte[])pixels.Rgba.Clone(), takeOwnership: true);
        return BImageHandle.FromId(id, new BSize(pixels.Width, pixels.Height));
    }

    public void ReleaseImage(BImageHandle image)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!image.IsValid || !_images.TryRemove(image.Handle.Id, out BBitmap? bitmap))
            return;

        bitmap.Dispose();
    }

    public void Render(IBroilerSurface surface, BRenderList renderList, BFrameContext frameContext)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(renderList);

        if (surface is not BImageSurface imageSurface)
            throw new ArgumentException("Surface was not created by this renderer.", nameof(surface));

        renderList.Validate();
        var state = new ReplayState(imageSurface.DpiScale);
        using BCanvas canvas = imageSurface.Bitmap.OpenCanvas();
        canvas.Clear(frameContext.ClearColor);

        foreach (BRenderCommand command in renderList.Commands)
            Dispatch(canvas, command, state);
    }

    public BBitmap RenderToImage(BRenderList renderList, BSurfaceDescriptor descriptor, BFrameContext frameContext)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(renderList);

        using var surface = new BImageSurface(descriptor);
        Render(surface, renderList, frameContext);
        return surface.Bitmap.Copy();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        foreach (BBitmap bitmap in _images.Values)
            bitmap.Dispose();

        _images.Clear();
    }

    private void Dispatch(BCanvas canvas, BRenderCommand command, ReplayState state)
    {
        switch (command)
        {
            case BRenderCommand.FillRect c:
                FillRect(canvas, c, state);
                break;
            case BRenderCommand.StrokeRect c:
                StrokeRect(canvas, c, state);
                break;
            case BRenderCommand.FillRoundedRect c:
                FillRoundedRect(canvas, c, state);
                break;
            case BRenderCommand.StrokeRoundedRect c:
                StrokeRoundedRect(canvas, c, state);
                break;
            case BRenderCommand.FillTriangle c:
                FillTriangle(canvas, c, state);
                break;
            case BRenderCommand.DrawText c:
                DrawText(canvas, c, state);
                break;
            case BRenderCommand.DrawImage c:
                DrawImage(canvas, c, state);
                break;
            case BRenderCommand.PushClip c:
                canvas.PushClip(TransformRect(c.Rect, state));
                break;
            case BRenderCommand.PopClip:
                canvas.PopClip();
                break;
            case BRenderCommand.PushTransform c:
                state.TransformStack.Push(state.CurrentTransform);
                state.CurrentTransform = state.CurrentTransform * c.Transform;
                break;
            case BRenderCommand.PopTransform:
                state.CurrentTransform = state.TransformStack.Pop();
                break;
            default:
                throw new NotSupportedException($"Unknown render command: {command.GetType().Name}");
        }
    }

    private void FillRect(BCanvas canvas, BRenderCommand.FillRect command, ReplayState state)
    {
        if (command.Color.A == 0)
            return;

        // A rotated or skewed rectangle is a quad, not a rectangle. TransformRect answers with the
        // axis-aligned bounding box, which is right for an upright transform and badly wrong for
        // any other: a thin diagonal strip came out as the solid block that encloses it, so a
        // rotated element filled its whole bounding box and a diagonal rule became a rectangle.
        // The canvas can fill an arbitrary polygon, so hand it the four transformed corners.
        if (IsAxisAligned(state.Effective))
        {
            RectangleF rect = TransformRect(command.Rect, state);
            if (IsDrawable(rect))
                canvas.FillRect(rect, command.Color);

            return;
        }

        PointF[]? quad = TransformQuad(command.Rect, state);
        if (quad is not null)
            canvas.FillPolygon(quad, command.Color);
    }

    /// <summary>
    /// Fills a triangle through the same antialiased scanline filler glyphs use, so a diagonal
    /// edge is smooth rather than stepped. FillPolygon is deliberately not used here: it decides
    /// each pixel by a point-in-polygon test at the pixel centre, which is exactly the hard edge a
    /// triangle primitive exists to avoid.
    /// </summary>
    private void FillTriangle(BCanvas canvas, BRenderCommand.FillTriangle command, ReplayState state)
    {
        if (command.Color.A == 0)
            return;

        BMatrix3x2 transform = state.Effective;
        BPoint a = transform.Transform(command.A);
        BPoint b = transform.Transform(command.B);
        BPoint c = transform.Transform(command.C);
        if (!IsFinite(a) || !IsFinite(b) || !IsFinite(c))
            return;

        PointF[] contour =
        [
            new PointF((float)a.X, (float)a.Y),
            new PointF((float)b.X, (float)b.Y),
            new PointF((float)c.X, (float)c.Y),
        ];

        // Nonzero winding, so the corner order does not decide whether the triangle is filled.
        canvas.FillGlyphContours([contour], command.Color);
    }

    /// <summary>
    /// Whether the transform maps rectangles to rectangles — no rotation and no skew, so an
    /// axis-aligned fill is exact and the polygon path is unnecessary.
    /// </summary>
    private static bool IsAxisAligned(BMatrix3x2 transform) =>
        Math.Abs(transform.M12) < 1e-6 && Math.Abs(transform.M21) < 1e-6;

    /// <summary>The rectangle's four corners in device space, or null when it is degenerate.</summary>
    private static PointF[]? TransformQuad(BRect rect, ReplayState state)
    {
        if (!(rect.Width > 0) || !(rect.Height > 0))
            return null;

        BMatrix3x2 transform = state.Effective;
        BPoint p1 = transform.Transform(new BPoint(rect.Left, rect.Top));
        BPoint p2 = transform.Transform(new BPoint(rect.Right, rect.Top));
        BPoint p3 = transform.Transform(new BPoint(rect.Right, rect.Bottom));
        BPoint p4 = transform.Transform(new BPoint(rect.Left, rect.Bottom));

        if (!IsFinite(p1) || !IsFinite(p2) || !IsFinite(p3) || !IsFinite(p4))
            return null;

        return
        [
            new PointF((float)p1.X, (float)p1.Y),
            new PointF((float)p2.X, (float)p2.Y),
            new PointF((float)p3.X, (float)p3.Y),
            new PointF((float)p4.X, (float)p4.Y),
        ];
    }

    private static bool IsFinite(BPoint point) =>
        double.IsFinite(point.X) && double.IsFinite(point.Y);

    private void StrokeRect(BCanvas canvas, BRenderCommand.StrokeRect command, ReplayState state)
    {
        if (command.Color.A == 0 || command.Thickness <= 0)
            return;

        RectangleF rect = TransformRect(command.Rect, state);
        if (!IsDrawable(rect))
            return;

        canvas.DrawRectangleStroke(rect, command.Color, (float)Math.Max(1.0, command.Thickness * CurrentAverageScale(state)));
    }

    private void FillRoundedRect(BCanvas canvas, BRenderCommand.FillRoundedRect command, ReplayState state)
    {
        if (command.Color.A == 0)
            return;

        RectangleF rect = TransformRect(command.Rect, state);
        if (!IsDrawable(rect))
            return;

        float scale = (float)CurrentAverageScale(state);
        canvas.FillRoundedRect(rect, command.Color, (float)(command.RadiusX * scale), (float)(command.RadiusY * scale));
    }

    private void StrokeRoundedRect(BCanvas canvas, BRenderCommand.StrokeRoundedRect command, ReplayState state)
    {
        if (command.Color.A == 0 || command.Thickness <= 0)
            return;

        RectangleF rect = TransformRect(command.Rect, state);
        if (!IsDrawable(rect))
            return;

        float scale = (float)CurrentAverageScale(state);
        canvas.DrawRoundedRectangleStroke(
            rect,
            command.Color,
            (float)(command.RadiusX * scale),
            (float)(command.RadiusY * scale),
            (float)Math.Max(1.0, command.Thickness * scale));
    }

    private void DrawImage(BCanvas canvas, BRenderCommand.DrawImage command, ReplayState state)
    {
        if (command.Opacity <= 0)
            return;

        if (!command.Image.IsValid || !_images.TryGetValue(command.Image.Handle.Id, out BBitmap? image))
            throw new ArgumentException($"Image #{command.Image.Handle.Id} was not created by this renderer.", nameof(command));

        RectangleF source = ToRectangleF(command.Source);
        if (!IsDrawable(source))
            source = new RectangleF(0, 0, image.Width, image.Height);

        RectangleF destination = TransformRect(command.Destination, state);
        if (!IsDrawable(destination))
            return;

        if (command.Opacity < 1.0)
            canvas.SaveOpacityLayer((float)command.Opacity);

        canvas.DrawBitmap(image, destination, source);

        if (command.Opacity < 1.0)
            canvas.RestoreOpacityLayer();
    }

    /// <summary>Reports which real font (if any) backs text rendering; for host diagnostics.</summary>
    public static string DescribeSystemTextFont() => FallbackSystemFont.Describe();

    private void DrawText(BCanvas canvas, BRenderCommand.DrawText command, ReplayState state)
    {
        BTextRun run = command.Text;
        if (string.IsNullOrEmpty(run.Text) || run.Color.A == 0 || run.Font.SizeInPixels <= 0)
            return;

        double fontSize = Math.Max(1.0, run.Font.SizeInPixels);
        bool bold = run.Font.Weight >= BFontWeight.Bold;
        bool italic = run.Font.Slant is BFontSlant.Italic or BFontSlant.Oblique;

        // Prefer the host's face for the family that was actually asked for, then any host face,
        // then the built-in block font so text still renders on a font-less system. Resolving the
        // family matters because whoever laid this text out measured it in that family: drawing it
        // in another makes each run the wrong width for the space reserved for it. Must stay in
        // step with BTextMeasurer's fallback provider, which resolves the same way.
        FallbackSystemFont? font = FallbackSystemFont.For(run.Font.FamilyName, bold, italic);
        if (font is not null)
            DrawTextWithSystemFont(canvas, run, command.Origin, fontSize, bold, font, state);
        else
            DrawTextWithBlockFont(canvas, run, command.Origin, fontSize, bold, state);
    }

    private void DrawTextWithBlockFont(BCanvas canvas, BTextRun run, BPoint origin, double fontSize, bool bold, ReplayState state)
    {
        double advance = Math.Max(1.0, fontSize * 0.62);
        double glyphHeight = fontSize;
        double glyphWidth = Math.Max(1.0, fontSize * 0.54);
        double penX = origin.X;

        foreach (char ch in run.Text)
        {
            if (ch is '\r' or '\n')
            {
                penX = origin.X;
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                penX += advance;
                continue;
            }

            DrawFallbackGlyph(canvas, ch, penX, origin.Y, glyphWidth, glyphHeight, run.Color, bold, state);
            penX += advance;
        }
    }

    private void DrawTextWithSystemFont(BCanvas canvas, BTextRun run, BPoint origin, double fontSize, bool bold, FallbackSystemFont font, ReplayState state)
    {
        double blockAdvance = Math.Max(1.0, fontSize * 0.62);
        double glyphHeight = fontSize;
        double glyphWidth = Math.Max(1.0, fontSize * 0.54);
        // Match BTextMeasurer's baseline assumption so real glyphs sit where the
        // UI layout expects text to be.
        double baseline = origin.Y + (fontSize * 0.8);
        double penX = origin.X;

        foreach (char ch in run.Text)
        {
            if (ch is '\r' or '\n')
            {
                penX = origin.X;
                continue;
            }

            if (font.TryGetGlyph(ch, bold, out IReadOnlyList<PointF[]> contours, out int advanceWidth, out int unitsPerEm))
            {
                double scale = fontSize / unitsPerEm;
                double step = advanceWidth > 0 ? advanceWidth * scale : blockAdvance;
                if (!char.IsWhiteSpace(ch) && contours.Count > 0)
                    FillScaledGlyph(canvas, contours, penX, baseline, scale, run.Color, state);

                penX += step;
                continue;
            }

            // The real font has no glyph for this codepoint: use the block glyph.
            if (char.IsWhiteSpace(ch))
            {
                penX += blockAdvance;
                continue;
            }

            DrawFallbackGlyph(canvas, ch, penX, origin.Y, glyphWidth, glyphHeight, run.Color, bold, state);
            penX += blockAdvance;
        }
    }

    private static void FillScaledGlyph(BCanvas canvas, IReadOnlyList<PointF[]> contours, double penX, double baseline, double scale, BColor color, ReplayState state)
    {
        BMatrix3x2 transform = state.Effective;
        var device = new PointF[contours.Count][];
        for (int c = 0; c < contours.Count; c++)
        {
            PointF[] source = contours[c];
            var destination = new PointF[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                // Font outlines are y-up; the surface is y-down, so mirror around
                // the baseline before applying the render transform.
                double lx = penX + (source[i].X * scale);
                double ly = baseline - (source[i].Y * scale);
                BPoint transformed = transform.Transform(new BPoint(lx, ly));
                destination[i] = new PointF((float)transformed.X, (float)transformed.Y);
            }

            device[c] = destination;
        }

        canvas.FillGlyphContours(device, color);
    }

    private static void DrawFallbackGlyph(
        BCanvas canvas,
        char ch,
        double x,
        double y,
        double width,
        double height,
        BColor color,
        bool bold,
        ReplayState state)
    {
        string[] pattern = GlyphPattern(ch);
        double cellW = width / 5.0;
        double cellH = height / 7.0;
        double inflate = bold ? Math.Max(1.0, cellW * 0.18) : 0.0;

        for (int row = 0; row < pattern.Length; row++)
        {
            string line = pattern[row];
            for (int col = 0; col < line.Length; col++)
            {
                if (line[col] == ' ')
                    continue;

                RectangleF rect = TransformRect(new BRect(
                    x + (col * cellW),
                    y + (row * cellH),
                    cellW + inflate,
                    cellH), state);
                if (IsDrawable(rect))
                    canvas.FillRect(rect, color);
            }
        }
    }

    private static RectangleF TransformRect(BRect rect, ReplayState state)
    {
        BMatrix3x2 transform = state.Effective;
        BPoint p1 = transform.Transform(new BPoint(rect.Left, rect.Top));
        BPoint p2 = transform.Transform(new BPoint(rect.Right, rect.Top));
        BPoint p3 = transform.Transform(new BPoint(rect.Right, rect.Bottom));
        BPoint p4 = transform.Transform(new BPoint(rect.Left, rect.Bottom));

        double left = Math.Min(Math.Min(p1.X, p2.X), Math.Min(p3.X, p4.X));
        double top = Math.Min(Math.Min(p1.Y, p2.Y), Math.Min(p3.Y, p4.Y));
        double right = Math.Max(Math.Max(p1.X, p2.X), Math.Max(p3.X, p4.X));
        double bottom = Math.Max(Math.Max(p1.Y, p2.Y), Math.Max(p3.Y, p4.Y));

        return new RectangleF((float)left, (float)top, (float)(right - left), (float)(bottom - top));
    }

    private static double CurrentAverageScale(ReplayState state)
    {
        BMatrix3x2 transform = state.Effective;
        double x = Math.Sqrt((transform.M11 * transform.M11) + (transform.M12 * transform.M12));
        double y = Math.Sqrt((transform.M21 * transform.M21) + (transform.M22 * transform.M22));
        double scale = (x + y) / 2.0;
        return scale > 0 && !double.IsNaN(scale) && !double.IsInfinity(scale) ? scale : 1.0;
    }

    private static RectangleF ToRectangleF(BRect rect) =>
        new((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height);

    private static bool IsDrawable(RectangleF rect) =>
        rect.Width > 0
        && rect.Height > 0
        && float.IsFinite(rect.X)
        && float.IsFinite(rect.Y)
        && float.IsFinite(rect.Width)
        && float.IsFinite(rect.Height);

    private static string[] GlyphPattern(char ch) => ch switch
    {
        'A' => [" ### ", "#   #", "#   #", "#####", "#   #", "#   #", "#   #"],
        'B' => ["#### ", "#   #", "#   #", "#### ", "#   #", "#   #", "#### "],
        'C' => [" ####", "#    ", "#    ", "#    ", "#    ", "#    ", " ####"],
        'D' => ["#### ", "#   #", "#   #", "#   #", "#   #", "#   #", "#### "],
        'E' => ["#####", "#    ", "#    ", "#### ", "#    ", "#    ", "#####"],
        'F' => ["#####", "#    ", "#    ", "#### ", "#    ", "#    ", "#    "],
        'G' => [" ####", "#    ", "#    ", "# ###", "#   #", "#   #", " ####"],
        'H' => ["#   #", "#   #", "#   #", "#####", "#   #", "#   #", "#   #"],
        'I' => ["#####", "  #  ", "  #  ", "  #  ", "  #  ", "  #  ", "#####"],
        'J' => ["#####", "    #", "    #", "    #", "#   #", "#   #", " ### "],
        'K' => ["#   #", "#  # ", "# #  ", "##   ", "# #  ", "#  # ", "#   #"],
        'L' => ["#    ", "#    ", "#    ", "#    ", "#    ", "#    ", "#####"],
        'M' => ["#   #", "## ##", "# # #", "#   #", "#   #", "#   #", "#   #"],
        'N' => ["#   #", "##  #", "# # #", "#  ##", "#   #", "#   #", "#   #"],
        'O' => [" ### ", "#   #", "#   #", "#   #", "#   #", "#   #", " ### "],
        'P' => ["#### ", "#   #", "#   #", "#### ", "#    ", "#    ", "#    "],
        'Q' => [" ### ", "#   #", "#   #", "#   #", "# # #", "#  # ", " ## #"],
        'R' => ["#### ", "#   #", "#   #", "#### ", "# #  ", "#  # ", "#   #"],
        'S' => [" ####", "#    ", "#    ", " ### ", "    #", "    #", "#### "],
        'T' => ["#####", "  #  ", "  #  ", "  #  ", "  #  ", "  #  ", "  #  "],
        'U' => ["#   #", "#   #", "#   #", "#   #", "#   #", "#   #", " ### "],
        'V' => ["#   #", "#   #", "#   #", "#   #", "#   #", " # # ", "  #  "],
        'W' => ["#   #", "#   #", "#   #", "# # #", "# # #", "## ##", "#   #"],
        'X' => ["#   #", "#   #", " # # ", "  #  ", " # # ", "#   #", "#   #"],
        'Y' => ["#   #", "#   #", " # # ", "  #  ", "  #  ", "  #  ", "  #  "],
        'Z' => ["#####", "    #", "   # ", "  #  ", " #   ", "#    ", "#####"],
        '0' => [" ### ", "#   #", "#  ##", "# # #", "##  #", "#   #", " ### "],
        '1' => ["  #  ", " ##  ", "# #  ", "  #  ", "  #  ", "  #  ", "#####"],
        '2' => [" ### ", "#   #", "    #", "   # ", "  #  ", " #   ", "#####"],
        '3' => ["#### ", "    #", "    #", " ### ", "    #", "    #", "#### "],
        '4' => ["#   #", "#   #", "#   #", "#####", "    #", "    #", "    #"],
        '5' => ["#####", "#    ", "#    ", "#### ", "    #", "    #", "#### "],
        '6' => [" ### ", "#    ", "#    ", "#### ", "#   #", "#   #", " ### "],
        '7' => ["#####", "    #", "   # ", "  #  ", " #   ", " #   ", " #   "],
        '8' => [" ### ", "#   #", "#   #", " ### ", "#   #", "#   #", " ### "],
        '9' => [" ### ", "#   #", "#   #", " ####", "    #", "    #", " ### "],
        '.' => ["     ", "     ", "     ", "     ", "     ", " ##  ", " ##  "],
        ',' => ["     ", "     ", "     ", "     ", " ##  ", " ##  ", " #   "],
        ':' => ["     ", " ##  ", " ##  ", "     ", " ##  ", " ##  ", "     "],
        ';' => ["     ", " ##  ", " ##  ", "     ", " ##  ", " ##  ", " #   "],
        '!' => ["  #  ", "  #  ", "  #  ", "  #  ", "  #  ", "     ", "  #  "],
        '?' => [" ### ", "#   #", "    #", "   # ", "  #  ", "     ", "  #  "],
        '-' => ["     ", "     ", "     ", "#####", "     ", "     ", "     "],
        '_' => ["     ", "     ", "     ", "     ", "     ", "     ", "#####"],
        '+' => ["     ", "  #  ", "  #  ", "#####", "  #  ", "  #  ", "     "],
        '/' => ["    #", "    #", "   # ", "  #  ", " #   ", "#    ", "#    "],
        '\\' => ["#    ", "#    ", " #   ", "  #  ", "   # ", "    #", "    #"],
        '(' => ["   # ", "  #  ", " #   ", " #   ", " #   ", "  #  ", "   # "],
        ')' => [" #   ", "  #  ", "   # ", "   # ", "   # ", "  #  ", " #   "],
        '[' => [" ### ", " #   ", " #   ", " #   ", " #   ", " #   ", " ### "],
        ']' => [" ### ", "   # ", "   # ", "   # ", "   # ", "   # ", " ### "],
        '<' => ["   # ", "  #  ", " #   ", "#    ", " #   ", "  #  ", "   # "],
        '>' => [" #   ", "  #  ", "   # ", "    #", "   # ", "  #  ", " #   "],
        '=' => ["     ", "     ", "#####", "     ", "#####", "     ", "     "],
        '"' => [" # # ", " # # ", " # # ", "     ", "     ", "     ", "     "],
        '\'' => ["  #  ", "  #  ", "  #  ", "     ", "     ", "     ", "     "],
        '#' => [" # # ", " # # ", "#####", " # # ", "#####", " # # ", " # # "],
        'a' => ["     ", "     ", " ### ", "    #", " ####", "#   #", " ####"],
        'b' => ["#    ", "#    ", "#### ", "#   #", "#   #", "#   #", "#### "],
        'c' => ["     ", "     ", " ####", "#    ", "#    ", "#    ", " ####"],
        'd' => ["    #", "    #", " ####", "#   #", "#   #", "#   #", " ####"],
        'e' => ["     ", "     ", " ### ", "#   #", "#####", "#    ", " ### "],
        'f' => ["  ## ", " #   ", "#### ", " #   ", " #   ", " #   ", " #   "],
        'g' => ["     ", "     ", " ### ", "#   #", " ####", "    #", " ### "],
        'h' => ["#    ", "#    ", "#### ", "#   #", "#   #", "#   #", "#   #"],
        'i' => ["  #  ", "     ", " ##  ", "  #  ", "  #  ", "  #  ", " ### "],
        'j' => ["   # ", "     ", "  ## ", "   # ", "   # ", "#  # ", " ##  "],
        'k' => ["#    ", "#    ", "#  # ", "# #  ", "##   ", "# #  ", "#  # "],
        'l' => [" ##  ", "  #  ", "  #  ", "  #  ", "  #  ", "  #  ", " ### "],
        'm' => ["     ", "     ", "## # ", "# # #", "# # #", "# # #", "# # #"],
        'n' => ["     ", "     ", "#### ", "#   #", "#   #", "#   #", "#   #"],
        'o' => ["     ", "     ", " ### ", "#   #", "#   #", "#   #", " ### "],
        'p' => ["     ", "     ", "#### ", "#   #", "#### ", "#    ", "#    "],
        'q' => ["     ", "     ", " ####", "#   #", " ####", "    #", "    #"],
        'r' => ["     ", "     ", "# ## ", "##   ", "#    ", "#    ", "#    "],
        's' => ["     ", "     ", " ####", "#    ", " ### ", "    #", "#### "],
        't' => [" #   ", " #   ", "#### ", " #   ", " #   ", " #  #", "  ## "],
        'u' => ["     ", "     ", "#   #", "#   #", "#   #", "#   #", " ####"],
        'v' => ["     ", "     ", "#   #", "#   #", "#   #", " # # ", "  #  "],
        'w' => ["     ", "     ", "#   #", "# # #", "# # #", "# # #", " # # "],
        'x' => ["     ", "     ", "#   #", " # # ", "  #  ", " # # ", "#   #"],
        'y' => ["     ", "     ", "#   #", "#   #", " ####", "    #", " ### "],
        'z' => ["     ", "     ", "#####", "   # ", "  #  ", " #   ", "#####"],
        _ => ["#####", "#   #", "   # ", "  #  ", " #   ", "     ", "  #  "],
    };
}
