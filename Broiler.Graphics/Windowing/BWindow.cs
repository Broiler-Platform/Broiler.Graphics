using System;

namespace Broiler.Graphics;

/// <summary>
/// Abstract platform-neutral host for a rendered window. Backend packages provide concrete
/// implementations that own native windows and graphics resources.
/// </summary>
public abstract class BWindow : IDisposable
{
    private bool _disposed;

    protected BWindow(BWindowOptions options)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public BWindowOptions Options { get; }

    public bool IsDisposed => _disposed;

    public abstract IntPtr NativeHandle { get; }

    public abstract BSize ClientSize { get; }

    public abstract double DpiScale { get; }

    public abstract IBroilerRenderer? Renderer { get; }

    public abstract IBroilerSurface? Surface { get; }

    public int Run()
    {
        ThrowIfDisposed();
        return RunCore();
    }

    public void Invalidate()
    {
        ThrowIfDisposed();
        InvalidateCore();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Dispose(disposing: true);
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    protected void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    protected abstract int RunCore();

    protected abstract void InvalidateCore();

    protected virtual void OnCreated()
    {
    }

    protected virtual void OnResized(BSize clientSize, double dpiScale)
    {
    }

    protected virtual void OnGraphicsResourcesReleasing()
    {
    }

    protected virtual BFrameContext CreateFrameContext(long frameIndex) =>
        new(Options.ClearColor, frameIndex, Options.RenderOptions);

    protected abstract BRenderList? BuildRenderList(BSize clientSize);

    protected abstract void Dispose(bool disposing);
}
