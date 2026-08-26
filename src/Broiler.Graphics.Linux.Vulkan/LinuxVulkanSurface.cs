using System;

namespace Broiler.Graphics.Linux.Vulkan;

public sealed class LinuxVulkanSurface : ILinuxVulkanPresentSurface
{
    private readonly LinuxVulkanRendererOptions _options;
    private BSurfaceDescriptor _descriptor;
    private LinuxVulkanDeviceSession? _session;
    private BBitmap? _lastFrame;
    private string _diagnostic = "Vulkan presentation has not been initialized.";
    private bool _disposed;

    internal LinuxVulkanSurface(BSurfaceDescriptor descriptor, LinuxVulkanRendererOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _descriptor = ValidateDescriptor(descriptor);
        TryCreateSession();
    }

    public BSize Size => _descriptor.Size;

    public double DpiScale => _descriptor.DpiScale;

    public BSurfaceDescriptor Descriptor => _descriptor;

    public bool IsDeviceBacked => _session is not null;

    public string Diagnostic => _diagnostic;

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
            _session.AcknowledgeCpuPresentFrame(bitmap, vsync);
            _diagnostic = "CPU-present frame validated against Vulkan loader/device path. " + _session.Diagnostic;
        }
        catch (Exception exception) when (_options.AllowCpuFallbackWhenVulkanUnavailable)
        {
            _diagnostic = "Vulkan device path failed; using CPU-present fallback. " + exception.Message;
            RecreateSession(disableOnFailure: true);
        }
    }

    public BBitmap ReadToBitmap()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

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

        if (!_options.TryCreateVulkanDevice)
        {
            _diagnostic = "Vulkan device creation is disabled by renderer options.";
            return;
        }

        if (!OperatingSystem.IsLinux())
        {
            _diagnostic = "Vulkan loader/device creation requires Linux; using CPU-present fallback.";
            if (!_options.AllowCpuFallbackWhenVulkanUnavailable)
                throw new PlatformNotSupportedException(_diagnostic);

            return;
        }

        if (LinuxVulkanDeviceSession.TryCreate(_options, out LinuxVulkanDeviceSession? session, out string diagnostic))
        {
            _session = session;
            _diagnostic = diagnostic;
            return;
        }

        _diagnostic = diagnostic;
        if (!_options.AllowCpuFallbackWhenVulkanUnavailable)
            throw new LinuxVulkanException(diagnostic);
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
