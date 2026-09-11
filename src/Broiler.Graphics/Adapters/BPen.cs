using Broiler.Graphics.Rendering;

namespace Broiler.Graphics.Adapters;

public abstract class BPen
{
    public abstract double Width { get; set; }
    public abstract DashStyle DashStyle { set; }
}