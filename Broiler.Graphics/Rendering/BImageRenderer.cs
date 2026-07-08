using System;
using System.Collections.Generic;
using System.Drawing;

namespace Broiler.Graphics;

/// <summary>
/// Platform-neutral renderer that replays <see cref="BRenderList"/> commands into
/// <see cref="BImageSurface"/> / <see cref="BBitmap"/> targets.
/// </summary>
public sealed class BImageRenderer : IBroilerRenderer
{
    private readonly Dictionary<ulong, BBitmap> _images = [];
    private readonly Stack<BMatrix3x2> _transformStack = new();
    private BMatrix3x2 _currentTransform = BMatrix3x2.Identity;
    private BMatrix3x2 _pixelScaleTransform = BMatrix3x2.Identity;
    private ulong _nextImageId;
    private bool _disposed;

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

        ulong id = ++_nextImageId;
        _images[id] = new BBitmap(pixels.Width, pixels.Height, (byte[])pixels.Rgba.Clone(), takeOwnership: true);
        return BImageHandle.FromId(id, new BSize(pixels.Width, pixels.Height));
    }

    public void ReleaseImage(BImageHandle image)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!image.IsValid || !_images.Remove(image.Handle.Id, out BBitmap? bitmap))
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
        ResetManagedDrawingState(imageSurface.DpiScale);
        using BCanvas canvas = imageSurface.Bitmap.OpenCanvas();
        canvas.Clear(frameContext.ClearColor);

        try
        {
            foreach (BRenderCommand command in renderList.Commands)
                Dispatch(canvas, command);
        }
        finally
        {
            ResetManagedDrawingState(1.0);
        }
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

    private void Dispatch(BCanvas canvas, BRenderCommand command)
    {
        switch (command)
        {
            case BRenderCommand.FillRect c:
                FillRect(canvas, c);
                break;
            case BRenderCommand.StrokeRect c:
                StrokeRect(canvas, c);
                break;
            case BRenderCommand.FillRoundedRect c:
                FillRoundedRect(canvas, c);
                break;
            case BRenderCommand.StrokeRoundedRect c:
                StrokeRoundedRect(canvas, c);
                break;
            case BRenderCommand.DrawText c:
                DrawText(canvas, c);
                break;
            case BRenderCommand.DrawImage c:
                DrawImage(canvas, c);
                break;
            case BRenderCommand.PushClip c:
                canvas.PushClip(TransformRect(c.Rect));
                break;
            case BRenderCommand.PopClip:
                canvas.PopClip();
                break;
            case BRenderCommand.PushTransform c:
                _transformStack.Push(_currentTransform);
                _currentTransform = _currentTransform * c.Transform;
                break;
            case BRenderCommand.PopTransform:
                _currentTransform = _transformStack.Pop();
                break;
            default:
                throw new NotSupportedException($"Unknown render command: {command.GetType().Name}");
        }
    }

    private void FillRect(BCanvas canvas, BRenderCommand.FillRect command)
    {
        if (command.Color.A == 0)
            return;

        RectangleF rect = TransformRect(command.Rect);
        if (IsDrawable(rect))
            canvas.FillRect(rect, command.Color);
    }

    private void StrokeRect(BCanvas canvas, BRenderCommand.StrokeRect command)
    {
        if (command.Color.A == 0 || command.Thickness <= 0)
            return;

        RectangleF rect = TransformRect(command.Rect);
        if (!IsDrawable(rect))
            return;

        canvas.DrawRectangleStroke(rect, command.Color, (float)Math.Max(1.0, command.Thickness * CurrentAverageScale()));
    }

    private void FillRoundedRect(BCanvas canvas, BRenderCommand.FillRoundedRect command)
    {
        if (command.Color.A == 0)
            return;

        RectangleF rect = TransformRect(command.Rect);
        if (!IsDrawable(rect))
            return;

        float scale = (float)CurrentAverageScale();
        canvas.FillRoundedRect(rect, command.Color, (float)(command.RadiusX * scale), (float)(command.RadiusY * scale));
    }

    private void StrokeRoundedRect(BCanvas canvas, BRenderCommand.StrokeRoundedRect command)
    {
        if (command.Color.A == 0 || command.Thickness <= 0)
            return;

        RectangleF rect = TransformRect(command.Rect);
        if (!IsDrawable(rect))
            return;

        float scale = (float)CurrentAverageScale();
        canvas.DrawRoundedRectangleStroke(
            rect,
            command.Color,
            (float)(command.RadiusX * scale),
            (float)(command.RadiusY * scale),
            (float)Math.Max(1.0, command.Thickness * scale));
    }

    private void DrawImage(BCanvas canvas, BRenderCommand.DrawImage command)
    {
        if (command.Opacity <= 0)
            return;

        if (!command.Image.IsValid || !_images.TryGetValue(command.Image.Handle.Id, out BBitmap? image))
            throw new ArgumentException($"Image #{command.Image.Handle.Id} was not created by this renderer.", nameof(command));

        RectangleF source = ToRectangleF(command.Source);
        if (!IsDrawable(source))
            source = new RectangleF(0, 0, image.Width, image.Height);

        RectangleF destination = TransformRect(command.Destination);
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

    private void DrawText(BCanvas canvas, BRenderCommand.DrawText command)
    {
        BTextRun run = command.Text;
        if (string.IsNullOrEmpty(run.Text) || run.Color.A == 0 || run.Font.SizeInPixels <= 0)
            return;

        double fontSize = Math.Max(1.0, run.Font.SizeInPixels);
        bool bold = run.Font.Weight >= BFontWeight.Bold;

        // Prefer a real host font when one was discovered; otherwise fall back to
        // the built-in block font so text still renders on a font-less system.
        FallbackSystemFont? font = FallbackSystemFont.Shared;
        if (font is not null)
            DrawTextWithSystemFont(canvas, run, command.Origin, fontSize, bold, font);
        else
            DrawTextWithBlockFont(canvas, run, command.Origin, fontSize, bold);
    }

    private void DrawTextWithBlockFont(BCanvas canvas, BTextRun run, BPoint origin, double fontSize, bool bold)
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

            DrawFallbackGlyph(canvas, ch, penX, origin.Y, glyphWidth, glyphHeight, run.Color, bold);
            penX += advance;
        }
    }

    private void DrawTextWithSystemFont(BCanvas canvas, BTextRun run, BPoint origin, double fontSize, bool bold, FallbackSystemFont font)
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
                    FillScaledGlyph(canvas, contours, penX, baseline, scale, run.Color);

                penX += step;
                continue;
            }

            // The real font has no glyph for this codepoint: use the block glyph.
            if (char.IsWhiteSpace(ch))
            {
                penX += blockAdvance;
                continue;
            }

            DrawFallbackGlyph(canvas, ch, penX, origin.Y, glyphWidth, glyphHeight, run.Color, bold);
            penX += blockAdvance;
        }
    }

    private void FillScaledGlyph(BCanvas canvas, IReadOnlyList<PointF[]> contours, double penX, double baseline, double scale, BColor color)
    {
        BMatrix3x2 transform = _currentTransform * _pixelScaleTransform;
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

    private void DrawFallbackGlyph(
        BCanvas canvas,
        char ch,
        double x,
        double y,
        double width,
        double height,
        BColor color,
        bool bold)
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
                    cellH));
                if (IsDrawable(rect))
                    canvas.FillRect(rect, color);
            }
        }
    }

    private RectangleF TransformRect(BRect rect)
    {
        BMatrix3x2 transform = _currentTransform * _pixelScaleTransform;
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

    private double CurrentAverageScale()
    {
        BMatrix3x2 transform = _currentTransform * _pixelScaleTransform;
        double x = Math.Sqrt((transform.M11 * transform.M11) + (transform.M12 * transform.M12));
        double y = Math.Sqrt((transform.M21 * transform.M21) + (transform.M22 * transform.M22));
        double scale = (x + y) / 2.0;
        return scale > 0 && !double.IsNaN(scale) && !double.IsInfinity(scale) ? scale : 1.0;
    }

    private void ResetManagedDrawingState(double dpiScale)
    {
        _transformStack.Clear();
        _currentTransform = BMatrix3x2.Identity;
        _pixelScaleTransform = BMatrix3x2.Scale(dpiScale, dpiScale);
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
