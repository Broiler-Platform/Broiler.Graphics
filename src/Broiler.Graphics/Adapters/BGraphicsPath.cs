using Broiler.Graphics.Geometry;
using System;

namespace Broiler.Graphics.Adapters;

public abstract class BGraphicsPath : IDisposable
{
    public abstract void Start(double x, double y);
    public abstract void LineTo(double x, double y);
    public abstract void ArcTo(double x, double y, double size, Corner corner);
    public abstract void Dispose();
}