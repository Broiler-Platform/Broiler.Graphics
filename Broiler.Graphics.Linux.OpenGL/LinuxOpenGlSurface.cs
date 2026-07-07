using System;

namespace Broiler.Graphics.Linux.OpenGL;

public sealed class LinuxOpenGlSurface : ILinuxOpenGlPresentSurface
{
    private readonly LinuxOpenGlRendererOptions _options;
    private BSurfaceDescriptor _descriptor;
    private LinuxOpenGlCpuPresentSession? _session;
    private BBitmap? _lastFrame;
    private string _diagnostic = "OpenGL presentation has not been initialized.";
    private bool _disposed;

    internal LinuxOpenGlSurface(BSurfaceDescriptor descriptor, LinuxOpenGlRendererOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _descriptor = ValidateDescriptor(descriptor);
        TryCreateSession();
    }

    public BSize Size => _descriptor.Size;

    public double DpiScale => _descriptor.DpiScale;

    public BSurfaceDescriptor Descriptor => _descriptor;

    public bool IsGpuBacked => _session is not null;

    public string Diagnostic => _diagnostic;

    public LinuxOpenGlDriverInfo? DriverInfo => _session?.DriverInfo;

    public bool TryReplayNative(BRenderList renderList, BFrameContext frameContext, bool vsync, out string diagnostic)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(renderList);

        diagnostic = string.Empty;
        if (!_options.EnableNativeCommandReplay)
        {
            diagnostic = "OpenGL native command replay is disabled by renderer options.";
            return false;
        }

        if (_session is null)
        {
            diagnostic = "OpenGL native command replay requires an active EGL/OpenGL session.";
            return false;
        }

        if (!LinuxOpenGlNativeReplay.TryCreatePlan(renderList, _descriptor, frameContext, out LinuxOpenGlNativeReplayPlan? plan, out diagnostic) || plan is null)
            return false;

        try
        {
            _lastFrame?.Dispose();
            _lastFrame = null;
            _session.ReplayNative(plan, PixelWidth, PixelHeight, vsync);
            _diagnostic = "Presented through OpenGL native command replay. " + diagnostic + " " + _session.DriverInfo.ToDiagnosticString();
            return true;
        }
        catch (Exception exception) when (_options.AllowCpuFallbackWhenOpenGlUnavailable)
        {
            diagnostic = "OpenGL native command replay failed; using CPU-present fallback. " + exception.Message;
            _diagnostic = diagnostic;
            RecreateSession(disableOnFailure: true);
            return false;
        }
    }

    public void Resize(BSize size, double dpiScale)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _descriptor = ValidateDescriptor(_descriptor with { Size = size, DpiScale = dpiScale });
        _lastFrame?.Dispose();
        _lastFrame = null;
        RecreateSession();
    }

    public void Present(BBitmap bitmap, bool vsync)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(bitmap);

        _lastFrame?.Dispose();
        _lastFrame = bitmap.Copy();

        if (_session is null)
            return;

        try
        {
            _session.Present(bitmap, PixelWidth, PixelHeight, vsync);
            _diagnostic = "Presented through EGL/OpenGL. " + _session.DriverInfo.ToDiagnosticString();
        }
        catch (Exception exception) when (_options.AllowCpuFallbackWhenOpenGlUnavailable)
        {
            _diagnostic = "OpenGL present failed; using CPU-present fallback. " + exception.Message;
            RecreateSession(disableOnFailure: true);
        }
    }

    public BBitmap ReadToBitmap()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_session is not null && _options.EnableGpuReadbackForRenderToImage)
        {
            try
            {
                return _session.ReadToBitmap();
            }
            catch (Exception exception) when (_options.AllowCpuFallbackWhenOpenGlUnavailable && _lastFrame is not null)
            {
                _diagnostic = "OpenGL readback failed; returning CPU-present fallback. " + exception.Message;
            }
        }

        if (_lastFrame is not null)
            return _lastFrame.Copy();

        return new BBitmap(PixelWidth, PixelHeight);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _session?.Dispose();
        _lastFrame?.Dispose();
    }

    private void TryCreateSession()
    {
        _session?.Dispose();
        _session = null;

        if (!_options.TryCreateEglContext)
        {
            _diagnostic = "EGL/OpenGL context creation is disabled by renderer options.";
            return;
        }

        if (!OperatingSystem.IsLinux())
        {
            _diagnostic = "EGL/OpenGL presentation requires Linux; using CPU-present fallback.";
            if (!_options.AllowCpuFallbackWhenOpenGlUnavailable)
                throw new PlatformNotSupportedException(_diagnostic);

            return;
        }

        if (LinuxOpenGlCpuPresentSession.TryCreatePbuffer(PixelWidth, PixelHeight, out LinuxOpenGlCpuPresentSession? session, out string diagnostic))
        {
            _session = session;
            _diagnostic = diagnostic;
            return;
        }

        _diagnostic = diagnostic;
        if (!_options.AllowCpuFallbackWhenOpenGlUnavailable)
            throw new LinuxOpenGlException(diagnostic);
    }

    private void RecreateSession(bool disableOnFailure = false)
    {
        _session?.Dispose();
        _session = null;

        if (disableOnFailure)
            return;

        TryCreateSession();
    }

    private int PixelWidth => ToPixelDimension(_descriptor.Size.Width, _descriptor.DpiScale, nameof(Size));

    private int PixelHeight => ToPixelDimension(_descriptor.Size.Height, _descriptor.DpiScale, nameof(Size));

    private static BSurfaceDescriptor ValidateDescriptor(BSurfaceDescriptor descriptor)
    {
        if (!IsPositiveFinite(descriptor.Size.Width) || !IsPositiveFinite(descriptor.Size.Height))
            throw new ArgumentOutOfRangeException(nameof(descriptor), "Surface size must be positive and finite.");

        return descriptor with
        {
            DpiScale = IsPositiveFinite(descriptor.DpiScale) ? descriptor.DpiScale : 1.0,
        };
    }

    private static int ToPixelDimension(double dip, double dpiScale, string name)
    {
        double pixels = Math.Ceiling(dip * dpiScale);
        if (pixels <= 0 || pixels > int.MaxValue)
            throw new ArgumentOutOfRangeException(name, "Surface pixel dimensions are outside the supported range.");

        return (int)pixels;
    }

    private static bool IsPositiveFinite(double value) =>
        value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
}
