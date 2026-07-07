using System.Drawing;

namespace Broiler.Graphics;

public interface IResourceFactory
{
    RPen GetPen(BColor color);
    RBrush GetSolidBrush(BColor color);
    RBrush GetLinearGradientBrush(RectangleF rect, BColor color1, BColor color2, double angle);
}
