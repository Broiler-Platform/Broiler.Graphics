using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Broiler.Graphics.Linux.Vulkan;

internal static class LinuxVulkanNative
{
    public const int VK_SUCCESS = 0;
    public const int VK_INCOMPLETE = 5;

    public const uint VK_STRUCTURE_TYPE_APPLICATION_INFO = 0;
    public const uint VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO = 1;
    public const uint VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO = 2;
    public const uint VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO = 3;

    public const uint VK_QUEUE_GRAPHICS_BIT = 0x00000001;

    public static uint MakeApiVersion(uint variant, int major, int minor, int patch)
    {
        if (major < 0 || major > 127)
            throw new ArgumentOutOfRangeException(nameof(major));
        if (minor < 0 || minor > 1023)
            throw new ArgumentOutOfRangeException(nameof(minor));
        if (patch < 0 || patch > 4095)
            throw new ArgumentOutOfRangeException(nameof(patch));

        return (variant << 29) | ((uint)major << 22) | ((uint)minor << 12) | (uint)patch;
    }

    public static uint GetSupportedInstanceVersion()
    {
        try
        {
            int result = EnumerateInstanceVersion(out uint version);
            ThrowIfFailed(result, "vkEnumerateInstanceVersion");
            return version;
        }
        catch (EntryPointNotFoundException)
        {
            return MakeApiVersion(0, 1, 0, 0);
        }
    }

    public static void ThrowIfFailed(int result, string operation)
    {
        if (result is VK_SUCCESS or VK_INCOMPLETE)
            return;

        throw new LinuxVulkanException($"{operation} failed with Vulkan result {ResultName(result)} ({result}).");
    }

    public static string FormatApiVersion(uint version)
    {
        uint major = (version >> 22) & 0x7F;
        uint minor = (version >> 12) & 0x3FF;
        uint patch = version & 0xFFF;
        return $"{major}.{minor}.{patch}";
    }

    public static LinuxVulkanDeviceInfo GetPhysicalDeviceInfo(IntPtr physicalDevice)
    {
        if (physicalDevice == IntPtr.Zero)
            throw new ArgumentException("A Vulkan physical-device handle is required.", nameof(physicalDevice));

        const int bufferSize = 4096;
        const int deviceNameOffset = 20;
        const int deviceNameLength = 256;
        IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            GetPhysicalDeviceProperties(physicalDevice, buffer);
            uint apiVersion = ReadUInt32(buffer, 0);
            uint driverVersion = ReadUInt32(buffer, 4);
            uint vendorId = ReadUInt32(buffer, 8);
            uint deviceId = ReadUInt32(buffer, 12);
            uint deviceType = ReadUInt32(buffer, 16);

            byte[] nameBytes = new byte[deviceNameLength];
            Marshal.Copy(IntPtr.Add(buffer, deviceNameOffset), nameBytes, 0, nameBytes.Length);
            int nameLength = Array.IndexOf(nameBytes, (byte)0);
            if (nameLength < 0)
                nameLength = nameBytes.Length;

            string name = Encoding.UTF8.GetString(nameBytes, 0, nameLength);
            if (string.IsNullOrWhiteSpace(name))
                name = "unknown";

            return new LinuxVulkanDeviceInfo(
                name,
                FormatPhysicalDeviceType(deviceType),
                FormatApiVersion(apiVersion),
                "0x" + driverVersion.ToString("X8"),
                vendorId,
                deviceId);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [DllImport("libvulkan.so.1", EntryPoint = "vkEnumerateInstanceVersion", CallingConvention = CallingConvention.Cdecl)]
    private static extern int EnumerateInstanceVersion(out uint apiVersion);

    [DllImport("libvulkan.so.1", EntryPoint = "vkCreateInstance", CallingConvention = CallingConvention.Cdecl)]
    public static extern int CreateInstance(
        ref VkInstanceCreateInfo createInfo,
        IntPtr allocator,
        out IntPtr instance);

    [DllImport("libvulkan.so.1", EntryPoint = "vkDestroyInstance", CallingConvention = CallingConvention.Cdecl)]
    public static extern void DestroyInstance(IntPtr instance, IntPtr allocator);

    [DllImport("libvulkan.so.1", EntryPoint = "vkEnumeratePhysicalDevices", CallingConvention = CallingConvention.Cdecl)]
    public static extern int EnumeratePhysicalDevices(
        IntPtr instance,
        ref uint physicalDeviceCount,
        [Out] IntPtr[]? physicalDevices);

    [DllImport("libvulkan.so.1", EntryPoint = "vkGetPhysicalDeviceQueueFamilyProperties", CallingConvention = CallingConvention.Cdecl)]
    public static extern void GetPhysicalDeviceQueueFamilyProperties(
        IntPtr physicalDevice,
        ref uint queueFamilyPropertyCount,
        [Out] VkQueueFamilyProperties[]? queueFamilyProperties);

    [DllImport("libvulkan.so.1", EntryPoint = "vkGetPhysicalDeviceProperties", CallingConvention = CallingConvention.Cdecl)]
    private static extern void GetPhysicalDeviceProperties(IntPtr physicalDevice, IntPtr properties);

    [DllImport("libvulkan.so.1", EntryPoint = "vkCreateDevice", CallingConvention = CallingConvention.Cdecl)]
    public static extern int CreateDevice(
        IntPtr physicalDevice,
        ref VkDeviceCreateInfo createInfo,
        IntPtr allocator,
        out IntPtr device);

    [DllImport("libvulkan.so.1", EntryPoint = "vkDestroyDevice", CallingConvention = CallingConvention.Cdecl)]
    public static extern void DestroyDevice(IntPtr device, IntPtr allocator);

    [DllImport("libvulkan.so.1", EntryPoint = "vkGetDeviceQueue", CallingConvention = CallingConvention.Cdecl)]
    public static extern void GetDeviceQueue(
        IntPtr device,
        uint queueFamilyIndex,
        uint queueIndex,
        out IntPtr queue);

    [DllImport("libvulkan.so.1", EntryPoint = "vkDeviceWaitIdle", CallingConvention = CallingConvention.Cdecl)]
    public static extern int DeviceWaitIdle(IntPtr device);

    [StructLayout(LayoutKind.Sequential)]
    public struct VkApplicationInfo
    {
        public uint SType;
        public IntPtr PNext;
        public IntPtr PApplicationName;
        public uint ApplicationVersion;
        public IntPtr PEngineName;
        public uint EngineVersion;
        public uint ApiVersion;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkInstanceCreateInfo
    {
        public uint SType;
        public IntPtr PNext;
        public uint Flags;
        public IntPtr PApplicationInfo;
        public uint EnabledLayerCount;
        public IntPtr PpEnabledLayerNames;
        public uint EnabledExtensionCount;
        public IntPtr PpEnabledExtensionNames;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkDeviceQueueCreateInfo
    {
        public uint SType;
        public IntPtr PNext;
        public uint Flags;
        public uint QueueFamilyIndex;
        public uint QueueCount;
        public IntPtr PQueuePriorities;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkDeviceCreateInfo
    {
        public uint SType;
        public IntPtr PNext;
        public uint Flags;
        public uint QueueCreateInfoCount;
        public IntPtr PQueueCreateInfos;
        public uint EnabledLayerCount;
        public IntPtr PpEnabledLayerNames;
        public uint EnabledExtensionCount;
        public IntPtr PpEnabledExtensionNames;
        public IntPtr PEnabledFeatures;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkQueueFamilyProperties
    {
        public uint QueueFlags;
        public uint QueueCount;
        public uint TimestampValidBits;
        public VkExtent3D MinImageTransferGranularity;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkExtent3D
    {
        public uint Width;
        public uint Height;
        public uint Depth;
    }

    private static string ResultName(int result) =>
        result switch
        {
            VK_SUCCESS => "VK_SUCCESS",
            VK_INCOMPLETE => "VK_INCOMPLETE",
            1 => "VK_NOT_READY",
            2 => "VK_TIMEOUT",
            3 => "VK_EVENT_SET",
            4 => "VK_EVENT_RESET",
            -1 => "VK_ERROR_OUT_OF_HOST_MEMORY",
            -2 => "VK_ERROR_OUT_OF_DEVICE_MEMORY",
            -3 => "VK_ERROR_INITIALIZATION_FAILED",
            -4 => "VK_ERROR_DEVICE_LOST",
            -5 => "VK_ERROR_MEMORY_MAP_FAILED",
            -6 => "VK_ERROR_LAYER_NOT_PRESENT",
            -7 => "VK_ERROR_EXTENSION_NOT_PRESENT",
            -8 => "VK_ERROR_FEATURE_NOT_PRESENT",
            -9 => "VK_ERROR_INCOMPATIBLE_DRIVER",
            -10 => "VK_ERROR_TOO_MANY_OBJECTS",
            -11 => "VK_ERROR_FORMAT_NOT_SUPPORTED",
            -1000000000 => "VK_ERROR_SURFACE_LOST_KHR",
            -1000000001 => "VK_ERROR_NATIVE_WINDOW_IN_USE_KHR",
            1000001003 => "VK_SUBOPTIMAL_KHR",
            -1000001004 => "VK_ERROR_OUT_OF_DATE_KHR",
            _ => "UNKNOWN",
        };

    private static uint ReadUInt32(IntPtr buffer, int offset) =>
        unchecked((uint)Marshal.ReadInt32(buffer, offset));

    private static string FormatPhysicalDeviceType(uint deviceType) =>
        deviceType switch
        {
            1 => "integrated-gpu",
            2 => "discrete-gpu",
            3 => "virtual-gpu",
            4 => "cpu",
            _ => "other",
        };
}
