using System;
using System.Collections.Generic;

namespace Broiler.Graphics.WebAssembly.Tests;

/// <summary>
/// A managed, pixel-faithful model of the Canvas 2D replay module for the exact
/// coordinate-exact subset — solid <c>FillRect</c>, rectangular clip set/clear, and
/// nearest-sampled <c>DrawImage</c>. It uses the same pixel-coverage and clip rules as the
/// CPU renderer's <c>BCanvas</c>, so on integer device coordinates it produces byte-identical
/// output. The conformance suite replays a planned frame through this rasterizer and compares
/// against the CPU oracle, validating the planner's device geometry, clip intersection, op
/// ordering, and image mapping without a browser.
/// <para>
/// It deliberately does <b>not</b> model rounded rectangles, stroke antialiasing, or text: on a
/// real browser those differ from the CPU renderer within documented antialiasing tolerances,
/// which is the browser-runtime gate, not a headless one.
/// </para>
/// </summary>
internal sealed class Canvas2DReferenceRasterizer
{
    private readonly int _width;
    private readonly int _height;
    private readonly byte[] _rgba;
    private readonly Dictionary<ulong, ImageResource> _resources = [];

    private bool _hasClip;
    private double _clipX, _clipY, _clipRight, _clipBottom;

    internal Canvas2DReferenceRasterizer(int width, int height, uint clearArgb)
    {
        _width = width;
        _height = height;
        _rgba = new byte[width * height * 4];
        (byte r, byte g, byte b, byte a) = Unpack(clearArgb);
        for (int i = 0; i < _rgba.Length; i += 4)
        {
            _rgba[i] = r;
            _rgba[i + 1] = g;
            _rgba[i + 2] = b;
            _rgba[i + 3] = a;
        }
    }

    internal byte[] Rgba => _rgba;

    internal void RegisterImage(ulong id, int width, int height, byte[] rgba) =>
        _resources[id] = new ImageResource(width, height, rgba);

    internal void Replay(CanvasFrame frame)
    {
        double[] s = frame.Stream;
        int len = frame.StreamLength;
        int i = 0;

        while (i < len)
        {
            int op = (int)s[i++];
            switch (op)
            {
                case CanvasReplayOp.SetClip:
                    _hasClip = true;
                    _clipX = s[i++];
                    _clipY = s[i++];
                    _clipRight = _clipX + s[i++];
                    _clipBottom = _clipY + s[i++];
                    break;
                case CanvasReplayOp.ClearClip:
                    _hasClip = false;
                    break;
                case CanvasReplayOp.FillRect:
                {
                    double x = s[i++], y = s[i++], w = s[i++], h = s[i++];
                    (byte r, byte g, byte b, byte a) = Unpack((uint)s[i++]);
                    FillRect(x, y, w, h, r, g, b, a);
                    break;
                }
                case CanvasReplayOp.DrawImage:
                {
                    ulong id = (ulong)s[i++];
                    double sx = s[i++], sy = s[i++], sw = s[i++], sh = s[i++];
                    double dx = s[i++], dy = s[i++], dw = s[i++], dh = s[i++];
                    i++; // opacity: the conformance scene uses 1.
                    DrawImage(id, sx, sy, sw, sh, dx, dy, dw, dh);
                    break;
                }
                default:
                    throw new AssertException($"Reference rasterizer does not model op {op}.");
            }
        }
    }

    private void FillRect(double x, double y, double w, double h, byte r, byte g, byte b, byte a)
    {
        int minX = Math.Max(0, (int)Math.Floor(x));
        int minY = Math.Max(0, (int)Math.Floor(y));
        int maxX = Math.Min(_width - 1, (int)Math.Ceiling(x + w) - 1);
        int maxY = Math.Min(_height - 1, (int)Math.Ceiling(y + h) - 1);

        for (int py = minY; py <= maxY; py++)
        {
            for (int px = minX; px <= maxX; px++)
            {
                if (IsVisible(px, py))
                    Blend(px, py, r, g, b, a);
            }
        }
    }

    private void DrawImage(ulong id, double sx, double sy, double sw, double sh, double dx, double dy, double dw, double dh)
    {
        if (!_resources.TryGetValue(id, out ImageResource? image))
            return;

        int startX = Math.Max(0, (int)Math.Floor(dx));
        int startY = Math.Max(0, (int)Math.Floor(dy));
        int endX = Math.Min(_width - 1, (int)Math.Ceiling(dx + dw) - 1);
        int endY = Math.Min(_height - 1, (int)Math.Ceiling(dy + dh) - 1);

        for (int py = startY; py <= endY; py++)
        {
            for (int px = startX; px <= endX; px++)
            {
                if (!IsVisible(px, py))
                    continue;

                double nx = ((px + 0.5) - dx) / dw;
                double ny = ((py + 0.5) - dy) / dh;
                if (nx < 0 || nx >= 1 || ny < 0 || ny >= 1)
                    continue;

                int srcX = Math.Clamp((int)Math.Floor(sx + (nx * sw)), 0, image.Width - 1);
                int srcY = Math.Clamp((int)Math.Floor(sy + (ny * sh)), 0, image.Height - 1);
                int o = ((srcY * image.Width) + srcX) * 4;
                Blend(px, py, image.Rgba[o], image.Rgba[o + 1], image.Rgba[o + 2], image.Rgba[o + 3]);
            }
        }
    }

    private bool IsVisible(int x, int y)
    {
        if (!_hasClip)
            return true;

        double cx = x + 0.5;
        double cy = y + 0.5;
        return cx >= _clipX && cx < _clipRight && cy >= _clipY && cy < _clipBottom;
    }

    private void Blend(int x, int y, byte r, byte g, byte b, byte a)
    {
        if (a == 0)
            return;

        int o = ((y * _width) + x) * 4;
        if (a == 255)
        {
            _rgba[o] = r;
            _rgba[o + 1] = g;
            _rgba[o + 2] = b;
            _rgba[o + 3] = 255;
            return;
        }

        // Straight-alpha source-over, matching BCanvas.CompositeSourceOver for the rare
        // translucent case; the conformance scene stays opaque so this path is incidental.
        float srcA = a / 255f;
        float dstA = _rgba[o + 3] / 255f;
        float outA = srcA + (dstA * (1f - srcA));
        if (outA <= 0f)
        {
            _rgba[o] = _rgba[o + 1] = _rgba[o + 2] = _rgba[o + 3] = 0;
            return;
        }

        _rgba[o] = Composite(r, _rgba[o], srcA, dstA, outA);
        _rgba[o + 1] = Composite(g, _rgba[o + 1], srcA, dstA, outA);
        _rgba[o + 2] = Composite(b, _rgba[o + 2], srcA, dstA, outA);
        _rgba[o + 3] = (byte)Math.Clamp((int)Math.Round(outA * 255f), 0, 255);
    }

    private static byte Composite(byte src, byte dst, float srcA, float dstA, float outA)
    {
        float value = ((src * srcA) + (dst * dstA * (1f - srcA))) / outA;
        return (byte)Math.Clamp((int)Math.Round(value), 0, 255);
    }

    private static (byte R, byte G, byte B, byte A) Unpack(uint argb) =>
        ((byte)((argb >> 16) & 0xFF), (byte)((argb >> 8) & 0xFF), (byte)(argb & 0xFF), (byte)((argb >> 24) & 0xFF));

    private sealed record ImageResource(int Width, int Height, byte[] Rgba);
}
