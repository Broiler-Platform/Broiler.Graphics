using System;

namespace Broiler.Graphics;

/// <summary>
/// A drawable target (window swap-chain, off-screen bitmap, etc.). Owns backend GPU resources and
/// must be disposed. All members are safe; native details live entirely inside the backend.
/// </summary>
public interface IBroilerSurface : IDisposable
{
    /// <summary>The logical size in device-independent units.</summary>
    BSize Size { get; }

    /// <summary>The DPI scale factor (1.0 == 96 DPI).</summary>
    double DpiScale { get; }

    /// <summary>Resizes the backing buffers. May recreate swap-chain resources.</summary>
    void Resize(BSize size, double dpiScale);
}
