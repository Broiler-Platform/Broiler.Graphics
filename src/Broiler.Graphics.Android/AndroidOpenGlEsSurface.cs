using System;

namespace Broiler.Graphics.Android;

/// <summary>
/// An off-screen Android surface backed by an EGL pbuffer.
/// </summary>
/// <remarks>
/// This is the headless path: it backs <see cref="IBroilerRenderer.RenderToImage"/> and gives tests
/// and capture tooling a real GPU round-trip without a window. The on-screen path is
/// <see cref="AndroidOpenGlEsWindowSurface"/>.
/// </remarks>
public sealed class AndroidOpenGlEsSurface : IAndroidPresentSurface
{
    private readonly AndroidOpenGlEsRendererOptions _options;
    private BSurfaceDescriptor _descriptor;
    private AndroidOpenGlEsSession? _session;
    private BBitmap? _lastFrame;
    private string _diagnostic = "OpenGL ES presentation has not been initialized.";
    private bool _disposed;

    internal AndroidOpenGlEsSurface(BSurfaceDescriptor descriptor, AndroidOpenGlEsRendererOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _descriptor = AndroidSurfaceGeometry.Validate(descriptor);
        TryCreateSession();
    }

    public BSize Size => _descriptor.Size;

    public double DpiScale => _descriptor.DpiScale;

    public BSurfaceDescriptor Descriptor => _descriptor;

    /// <summary>True when an EGL/OpenGL ES context is backing this surface.</summary>
    public bool IsGpuBacked => _session is not null;

    /// <summary>Human-readable description of the presentation route currently in use.</summary>
    public string Diagnostic => _diagnostic;

    public AndroidOpenGlEsDriverInfo? DriverInfo => _session?.DriverInfo;

    public void Resize(BSize size, double dpiScale)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _descriptor = AndroidSurfaceGeometry.Validate(_descriptor with { Size = size, DpiScale = dpiScale });
        _lastFrame?.Dispose();
        _lastFrame = null;

        _session?.Dispose();
        _session = null;
        TryCreateSession();
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
            _diagnostic = "Presented through EGL/OpenGL ES (pbuffer). " + _session.DriverInfo.ToDiagnosticString();
        }
        catch (Exception exception) when (exception is not BDeviceLostException && _options.AllowCpuFallbackWhenOpenGlUnavailable)
        {
            // The CPU frame is already retained above, so the surface keeps a correct image even
            // when the GPU path fails. Device loss deliberately escapes: the host has to rebuild.
            _diagnostic = "OpenGL ES present failed; holding the CPU frame. " + exception.Message;
            _session.Dispose();
            _session = null;
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
                _diagnostic = "OpenGL ES readback failed; returning the CPU frame. " + exception.Message;
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

    private int PixelWidth => AndroidSurfaceGeometry.ToPixels(_descriptor.Size.Width, _descriptor.DpiScale);

    private int PixelHeight => AndroidSurfaceGeometry.ToPixels(_descriptor.Size.Height, _descriptor.DpiScale);

    private void TryCreateSession()
    {
        if (!_options.TryCreateEglContext)
        {
            _diagnostic = "EGL/OpenGL ES context creation is disabled by renderer options.";
            return;
        }

        if (AndroidOpenGlEsSession.TryCreatePbuffer(PixelWidth, PixelHeight, out AndroidOpenGlEsSession? session, out string diagnostic))
        {
            _session = session;
            _diagnostic = diagnostic;
            return;
        }

        _diagnostic = diagnostic;
        if (!_options.AllowCpuFallbackWhenOpenGlUnavailable)
            throw new AndroidOpenGlEsException(diagnostic);
    }
}
