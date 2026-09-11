using Broiler.Graphics.Color;
using System.Drawing;

namespace Broiler.Graphics.Adapters;

public interface IResourceFactory
{
    BPen GetPen(BColor color);
    BBrush GetSolidBrush(BColor color);
    BBrush GetLinearGradientBrush(RectangleF rect, BColor color1, BColor color2, double angle);
}
