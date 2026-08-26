namespace Broiler.Graphics.Linux.Vulkan;

public sealed record LinuxVulkanRendererOptions(
    bool TryCreateVulkanDevice = true,
    bool AllowCpuFallbackWhenVulkanUnavailable = true,
    int RequiredApiMajor = 1,
    int RequiredApiMinor = 2)
{
    public static LinuxVulkanRendererOptions Default { get; } = new();
}
