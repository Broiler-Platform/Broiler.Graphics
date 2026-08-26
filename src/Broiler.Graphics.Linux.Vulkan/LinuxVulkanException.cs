using System;

namespace Broiler.Graphics.Linux.Vulkan;

public sealed class LinuxVulkanException : InvalidOperationException
{
    public LinuxVulkanException(string message)
        : base(message)
    {
    }

    public LinuxVulkanException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
