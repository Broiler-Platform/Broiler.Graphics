namespace Broiler.Graphics.Linux.Vulkan;

public sealed record LinuxVulkanDeviceInfo(
    string Name,
    string DeviceType,
    string ApiVersion,
    string DriverVersion,
    uint VendorId,
    uint DeviceId)
{
    public string ToDiagnosticString() =>
        $"Vulkan device={Name}; type={DeviceType}; api={ApiVersion}; driver={DriverVersion}; vendor=0x{VendorId:X4}; device=0x{DeviceId:X4}.";
}
