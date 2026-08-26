namespace Broiler.Graphics.Linux.Vulkan;

public interface ILinuxVulkanPresentSurface : IBroilerSurface
{
    BSurfaceDescriptor Descriptor { get; }

    bool IsDeviceBacked { get; }

    string Diagnostic { get; }

    void Present(BBitmap bitmap, bool vsync);

    BBitmap ReadToBitmap();
}
