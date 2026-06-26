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

    public BEditControl CreateEditControl(BControlOptions options)
    {
        ThrowIfDisposed();
        return CreateEditControlCore(options);
    }

    public BButtonControl CreateButtonControl(BControlOptions options)
    {
        ThrowIfDisposed();
        return CreateButtonControlCore(options);
    }

    /// <summary>
    /// Starts (or restarts) a repeating timer that drives <see cref="OnAnimationTick"/> on the UI
    /// thread roughly every <paramref name="intervalMilliseconds"/>. Used to step animations.
    /// </summary>
    public void StartAnimationTimer(double intervalMilliseconds)
    {
        ThrowIfDisposed();
        if (intervalMilliseconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(intervalMilliseconds));
        StartAnimationTimerCore(intervalMilliseconds);
    }

    /// <summary>Stops the timer previously started with <see cref="StartAnimationTimer"/>.</summary>
    public void StopAnimationTimer()
    {
        ThrowIfDisposed();
        StopAnimationTimerCore();
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

    protected abstract BEditControl CreateEditControlCore(BControlOptions options);

    protected abstract BButtonControl CreateButtonControlCore(BControlOptions options);

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

    /// <summary>Called when a mouse button is pressed over the render content area.</summary>
    protected virtual void OnPointerDown(BPointerEventArgs e)
    {
    }

    /// <summary>Called when the mouse moves over the render content area.</summary>
    protected virtual void OnPointerMove(BPointerEventArgs e)
    {
    }

    /// <summary>Called when a mouse button is released over the render content area.</summary>
    protected virtual void OnPointerUp(BPointerEventArgs e)
    {
    }

    /// <summary>Called when the mouse leaves the render content area.</summary>
    protected virtual void OnPointerLeave()
    {
    }

    /// <summary>Called when the mouse wheel is rotated over the render content area.</summary>
    protected virtual void OnMouseWheel(BMouseWheelEventArgs e)
    {
    }

    /// <summary>Called when a key is pressed while the render content area has focus.</summary>
    protected virtual void OnKeyDown(BKeyEventArgs e)
    {
    }

    /// <summary>Called when a key is released while the render content area has focus.</summary>
    protected virtual void OnKeyUp(BKeyEventArgs e)
    {
    }

    /// <summary>Called when a character is typed while the render content area has focus.</summary>
    protected virtual void OnTextInput(BTextInputEventArgs e)
    {
    }

    /// <summary>Called on each tick of the animation timer started with <see cref="StartAnimationTimer"/>.</summary>
    protected virtual void OnAnimationTick()
    {
    }

    /// <summary>Backend hook for <see cref="StartAnimationTimer"/>. Default implementation is unsupported.</summary>
    protected virtual void StartAnimationTimerCore(double intervalMilliseconds) =>
        throw new NotSupportedException("This window backend does not support animation timers.");

    /// <summary>Backend hook for <see cref="StopAnimationTimer"/>. Default implementation is a no-op.</summary>
    protected virtual void StopAnimationTimerCore()
    {
    }

    protected abstract void Dispose(bool disposing);
}
