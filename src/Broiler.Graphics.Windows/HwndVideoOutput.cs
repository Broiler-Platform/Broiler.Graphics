using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Broiler.Media;
using Broiler.Media.Video;

namespace Broiler.Graphics.Windows;

/// <summary>
/// A Windows HWND-backed video presentation target, declared and owned by
/// <c>Broiler.Graphics.Windows</c> (which owns the window lifetime). It implements the
/// platform-neutral <see cref="IVideoOutput"/> lifecycle so a video backend such as
/// <c>Broiler.Media.Video.MediaFoundation</c> can <em>borrow</em> it and drive
/// <c>IMFMediaEngine</c> against its HWND without ever creating or destroying the window.
/// </summary>
/// <remarks>
/// This type is the presentation-target contract required by the Broiler.Media roadmap
/// (§6.6 / §7.5): the HWND is declared and owned here in Graphics.Windows; the Media
/// Foundation implementation references this assembly and treats the handle's lifetime and
/// thread affinity as external constraints.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class HwndVideoOutput : IVideoOutput
{
    private readonly object _gate = new();

    public HwndVideoOutput(
        nint hwnd,
        string displayName,
        int width,
        int height,
        bool isVisible = true,
        bool validateNativeWindow = true)
    {
        if (hwnd == 0)
            throw new ArgumentException("A borrowed HWND must be non-zero.", nameof(hwnd));
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("A video target needs a display name.", nameof(displayName));
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));
        if (validateNativeWindow && !IsWindow(hwnd))
            throw new ArgumentException("The borrowed HWND is not a live native window.", nameof(hwnd));

        Hwnd = hwnd;
        DisplayName = displayName.Trim();
        Width = width;
        Height = height;
        IsVisible = isVisible;
    }

    public event EventHandler<HwndVideoTargetChangedEventArgs>? TargetChanged;

    public nint Hwnd { get; }

    public string DisplayName { get; }

    public int Width { get; private set; }

    public int Height { get; private set; }

    public bool IsVisible { get; private set; }

    public bool IsDestroyed { get; private set; }

    public bool Completed { get; private set; }

    public MediaError? Failure { get; private set; }

    public void Resize(int width, int height)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));

        lock (_gate)
        {
            ThrowIfDestroyed();
            Width = width;
            Height = height;
        }

        RaiseChanged(HwndVideoTargetChangeKind.Resized);
    }

    public void SetVisible(bool isVisible)
    {
        lock (_gate)
        {
            ThrowIfDestroyed();
            IsVisible = isVisible;
        }

        RaiseChanged(HwndVideoTargetChangeKind.VisibilityChanged);
    }

    public void NotifyDestroyed()
    {
        bool changed;
        lock (_gate)
        {
            changed = !IsDestroyed;
            IsDestroyed = true;
            IsVisible = false;
        }

        if (changed)
            RaiseChanged(HwndVideoTargetChangeKind.Destroyed);
    }

    public ValueTask CompleteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Completed = true;
        return ValueTask.CompletedTask;
    }

    public ValueTask FailAsync(MediaError error, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Failure = error ?? throw new ArgumentNullException(nameof(error));
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Throws <see cref="ObjectDisposedException"/> if the borrowed window has been
    /// destroyed by its owner. Callers (a borrowing video backend) invoke this before
    /// attaching or presenting to the HWND.
    /// </summary>
    public void ThrowIfUsableTargetRequired()
    {
        lock (_gate)
            ThrowIfDestroyed();
    }

    private void RaiseChanged(HwndVideoTargetChangeKind kind) =>
        TargetChanged?.Invoke(this, new HwndVideoTargetChangedEventArgs(kind, Width, Height, IsVisible));

    private void ThrowIfDestroyed()
    {
        if (IsDestroyed)
            throw new ObjectDisposedException(nameof(HwndVideoOutput), "The borrowed HWND has been destroyed by its owner.");
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint hwnd);
}
