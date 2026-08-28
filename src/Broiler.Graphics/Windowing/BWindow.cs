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

    /// <summary>
    /// Raised when the user asks the OS to close this window (close button, Alt+F4). A window that
    /// does not own the message loop (<see cref="BWindowOptions.OwnsMessageLoop"/> = false) is not
    /// destroyed by the request: the owner decides and calls <see cref="Close"/>.
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <summary>Raised after the native window has been destroyed.</summary>
    public event EventHandler? Closed;

    /// <summary>Raised when the window is minimized, maximized, or restored.</summary>
    public event EventHandler? StateChanged;

    public abstract IntPtr NativeHandle { get; }

    public abstract BSize ClientSize { get; }

    public abstract double DpiScale { get; }

    public abstract IBroilerRenderer? Renderer { get; }

    public abstract IBroilerSurface? Surface { get; }

    /// <summary>The current show state. <see cref="BWindowState.Normal"/> before the window exists.</summary>
    public virtual BWindowState WindowState => BWindowState.Normal;

    public int Run()
    {
        ThrowIfDisposed();
        return RunCore();
    }

    /// <summary>
    /// Realizes and shows the native window without entering a message loop. Used for a secondary
    /// window (<see cref="BWindowOptions.OwnsMessageLoop"/> = false) that an existing loop on the
    /// same thread services.
    /// </summary>
    public void Show()
    {
        ThrowIfDisposed();
        ShowCore();
    }

    /// <summary>Destroys the native window. Safe to call more than once.</summary>
    public void Close()
    {
        if (IsDisposed)
            return;

        CloseCore();
    }

    /// <summary>Sets the native window title. Owner-drawn chrome still uses it for the taskbar.</summary>
    public void SetTitle(string title)
    {
        ThrowIfDisposed();
        SetTitleCore(title ?? string.Empty);
    }

    /// <summary>
    /// Sets the window (taskbar and Alt+Tab) icon from straight-alpha RGBA pixels, or clears it
    /// when <paramref name="icon"/> is null. Owner-drawn chrome draws its own icon separately.
    /// </summary>
    public void SetIcon(BPixelBuffer? icon)
    {
        ThrowIfDisposed();
        SetIconCore(icon);
    }

    /// <summary>Minimizes, maximizes, or restores the window.</summary>
    public void SetWindowState(BWindowState state)
    {
        ThrowIfDisposed();
        SetWindowStateCore(state);
    }

    /// <summary>
    /// Hands an in-progress pointer press to the window manager as a window move. An owner-drawn
    /// title bar calls this on press so dragging, snapping, and shake behave natively.
    /// </summary>
    public void BeginMoveDrag()
    {
        ThrowIfDisposed();
        BeginMoveDragCore();
    }

    /// <summary>
    /// Hands an in-progress pointer press to the window manager as a resize of
    /// <paramref name="edge"/>. <see cref="BWindowEdge.None"/> is ignored.
    /// </summary>
    public void BeginResizeDrag(BWindowEdge edge)
    {
        ThrowIfDisposed();
        if (edge != BWindowEdge.None)
            BeginResizeDragCore(edge);
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

    public BLabelControl CreateLabelControl(BControlOptions options)
    {
        ThrowIfDisposed();
        return CreateLabelControlCore(options);
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

    protected void RaiseCloseRequested() => CloseRequested?.Invoke(this, EventArgs.Empty);

    protected void RaiseClosed() => Closed?.Invoke(this, EventArgs.Empty);

    protected void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    protected abstract int RunCore();

    /// <summary>Backend hook for <see cref="Show"/>. Default is unsupported.</summary>
    protected virtual void ShowCore() =>
        throw new NotSupportedException("This window backend does not support Show().");

    /// <summary>Backend hook for <see cref="Close"/>. Default is a no-op.</summary>
    protected virtual void CloseCore()
    {
    }

    /// <summary>Backend hook for <see cref="SetTitle"/>. Default is a no-op.</summary>
    protected virtual void SetTitleCore(string title)
    {
    }

    /// <summary>Backend hook for <see cref="SetIcon"/>. Default is a no-op.</summary>
    protected virtual void SetIconCore(BPixelBuffer? icon)
    {
    }

    /// <summary>Backend hook for <see cref="SetWindowState"/>. Default is a no-op.</summary>
    protected virtual void SetWindowStateCore(BWindowState state)
    {
    }

    /// <summary>Backend hook for <see cref="BeginMoveDrag"/>. Default is a no-op.</summary>
    protected virtual void BeginMoveDragCore()
    {
    }

    /// <summary>Backend hook for <see cref="BeginResizeDrag"/>. Default is a no-op.</summary>
    protected virtual void BeginResizeDragCore(BWindowEdge edge)
    {
    }

    protected abstract void InvalidateCore();

    protected abstract BEditControl CreateEditControlCore(BControlOptions options);

    protected abstract BButtonControl CreateButtonControlCore(BControlOptions options);

    protected abstract BLabelControl CreateLabelControlCore(BControlOptions options);

    protected virtual void OnCreated()
    {
    }

    protected virtual void OnResized(BSize clientSize, double dpiScale)
    {
    }

    protected virtual void OnGraphicsResourcesReleasing()
    {
    }

    /// <summary>Called when the native window is beginning final teardown.</summary>
    protected virtual void OnClosing()
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
