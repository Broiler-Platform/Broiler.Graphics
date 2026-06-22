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
        BImageCodec.UseManagedIfUnset();
        return CreateImage(BImageCodec.Decode(encodedImage));
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

    private void DrawText(BCanvas canvas, BRenderCommand.DrawText command)
    {
        BTextRun run = command.Text;
        if (string.IsNullOrEmpty(run.Text) || run.Color.A == 0 || run.Font.SizeInPixels <= 0)
            return;

        double fontSize = Math.Max(1.0, run.Font.SizeInPixels);
        double advance = Math.Max(1.0, fontSize * 0.62);
        double glyphHeight = fontSize;
        double glyphWidth = Math.Max(1.0, fontSize * 0.54);
        double penX = command.Origin.X;

        foreach (char ch in run.Text)
        {
            if (ch is '\r' or '\n')
            {
                penX = command.Origin.X;
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                penX += advance;
                continue;
            }

            DrawFallbackGlyph(
                canvas,
                char.ToUpperInvariant(ch),
                penX,
                command.Origin.Y,
                glyphWidth,
                glyphHeight,
                run.Color,
                run.Font.Weight >= BFontWeight.Bold);

            penX += advance;
        }
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
        _ => ["#####", "#   #", "   # ", "  #  ", " #   ", "     ", "  #  "],
    };
}
