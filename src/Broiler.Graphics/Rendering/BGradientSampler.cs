using Broiler.Graphics.Color;
using System;
using System.Collections.Generic;
using System.Drawing;

namespace Broiler.Graphics.Rendering;

/// <summary>Gradient stop normalization, geometry, and interpolation.</summary>
internal static class BGradientSampler
{
    internal static float[] NormalizeGradientPositions(int colorCount, IReadOnlyList<float>? positions)
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

    internal static (PointF StartPoint, PointF EndPoint) GetGradientEndpoints(RectangleF rect, float angle)
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

    internal static BColor SampleGradientColor(IReadOnlyList<BColor> colors, IReadOnlyList<float> positions, float t)
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
}
