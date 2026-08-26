using System;

namespace Broiler.Graphics;

/// <summary>Abstract base for native controls hosted by a <see cref="BWindow"/>.</summary>
public abstract class BControl : IDisposable
{
    public abstract IntPtr NativeHandle { get; }

    public abstract BRect Bounds { get; set; }

    public abstract string Text { get; set; }

    public abstract bool Enabled { get; set; }

    public abstract bool Visible { get; set; }

    public abstract void Focus();

    public abstract void Dispose();
}
