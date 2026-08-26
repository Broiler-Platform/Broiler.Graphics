using System;
using System.Runtime.InteropServices;

namespace Broiler.Graphics.Linux.Vulkan;

internal sealed class LinuxVulkanDeviceSession : IDisposable
{
    private readonly IntPtr _instance;
    private readonly IntPtr _physicalDevice;
    private readonly IntPtr _device;
    private readonly IntPtr _queue;
    private bool _disposed;

    private LinuxVulkanDeviceSession(
        IntPtr instance,
        IntPtr physicalDevice,
        IntPtr device,
        IntPtr queue,
        uint apiVersion,
        uint queueFamilyIndex,
        int physicalDeviceIndex,
        LinuxVulkanDeviceInfo deviceInfo)
    {
        _instance = instance;
        _physicalDevice = physicalDevice;
        _device = device;
        _queue = queue;
        ApiVersion = apiVersion;
        QueueFamilyIndex = queueFamilyIndex;
        PhysicalDeviceIndex = physicalDeviceIndex;
        DeviceInfo = deviceInfo;
        Diagnostic = $"Created Vulkan {FormatVersion(apiVersion)} logical device for physical device #{physicalDeviceIndex}, graphics queue family {queueFamilyIndex}. {deviceInfo.ToDiagnosticString()}";
    }

    public uint ApiVersion { get; }

    public uint QueueFamilyIndex { get; }

    public int PhysicalDeviceIndex { get; }

    public LinuxVulkanDeviceInfo DeviceInfo { get; }

    public string Diagnostic { get; }

    public static bool TryCreate(
        LinuxVulkanRendererOptions options,
        out LinuxVulkanDeviceSession? session,
        out string diagnostic)
    {
        ArgumentNullException.ThrowIfNull(options);
        session = null;
        diagnostic = string.Empty;

        try
        {
            session = Create(options);
            diagnostic = session.Diagnostic;
            return true;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or LinuxVulkanException)
        {
            diagnostic = "Could not create Vulkan loader/device path: " + exception.Message;
            return false;
        }
    }

    public void AcknowledgeCpuPresentFrame(BBitmap bitmap, bool vsync)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(bitmap);

        if (bitmap.Width <= 0 || bitmap.Height <= 0)
            throw new LinuxVulkanException("Cannot present an empty CPU frame.");

        int result = LinuxVulkanNative.DeviceWaitIdle(_device);
        LinuxVulkanNative.ThrowIfFailed(result, "vkDeviceWaitIdle");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_device != IntPtr.Zero)
        {
            _ = LinuxVulkanNative.DeviceWaitIdle(_device);
            LinuxVulkanNative.DestroyDevice(_device, IntPtr.Zero);
        }

        if (_instance != IntPtr.Zero)
            LinuxVulkanNative.DestroyInstance(_instance, IntPtr.Zero);
    }

    private static LinuxVulkanDeviceSession Create(LinuxVulkanRendererOptions options)
    {
        uint requiredVersion = LinuxVulkanNative.MakeApiVersion(0, options.RequiredApiMajor, options.RequiredApiMinor, 0);
        uint loaderVersion = LinuxVulkanNative.GetSupportedInstanceVersion();
        if (loaderVersion < requiredVersion)
        {
            throw new LinuxVulkanException(
                $"Vulkan {FormatVersion(requiredVersion)} is required by the Linux baseline, but the loader reports {FormatVersion(loaderVersion)}.");
        }

        IntPtr instance = CreateInstance(requiredVersion);
        try
        {
            PhysicalDeviceSelection selection = SelectPhysicalDevice(instance);
            IntPtr device = CreateDevice(selection.PhysicalDevice, selection.QueueFamilyIndex);
            try
            {
                LinuxVulkanNative.GetDeviceQueue(device, selection.QueueFamilyIndex, 0, out IntPtr queue);
                if (queue == IntPtr.Zero)
                    throw new LinuxVulkanException("vkGetDeviceQueue returned a null queue handle.");

                return new LinuxVulkanDeviceSession(
                    instance,
                    selection.PhysicalDevice,
                    device,
                    queue,
                    requiredVersion,
                    selection.QueueFamilyIndex,
                    selection.PhysicalDeviceIndex,
                    selection.DeviceInfo);
            }
            catch
            {
                LinuxVulkanNative.DestroyDevice(device, IntPtr.Zero);
                throw;
            }
        }
        catch
        {
            LinuxVulkanNative.DestroyInstance(instance, IntPtr.Zero);
            throw;
        }
    }

    private static IntPtr CreateInstance(uint apiVersion)
    {
        IntPtr applicationName = Marshal.StringToCoTaskMemUTF8("Broiler.Graphics.Linux");
        IntPtr engineName = Marshal.StringToCoTaskMemUTF8("Broiler");
        try
        {
            var applicationInfo = new LinuxVulkanNative.VkApplicationInfo
            {
                SType = LinuxVulkanNative.VK_STRUCTURE_TYPE_APPLICATION_INFO,
                PNext = IntPtr.Zero,
                PApplicationName = applicationName,
                ApplicationVersion = 1,
                PEngineName = engineName,
                EngineVersion = 1,
                ApiVersion = apiVersion,
            };

            IntPtr applicationInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<LinuxVulkanNative.VkApplicationInfo>());
            try
            {
                Marshal.StructureToPtr(applicationInfo, applicationInfoPtr, fDeleteOld: false);
                var createInfo = new LinuxVulkanNative.VkInstanceCreateInfo
                {
                    SType = LinuxVulkanNative.VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO,
                    PNext = IntPtr.Zero,
                    Flags = 0,
                    PApplicationInfo = applicationInfoPtr,
                    EnabledLayerCount = 0,
                    PpEnabledLayerNames = IntPtr.Zero,
                    EnabledExtensionCount = 0,
                    PpEnabledExtensionNames = IntPtr.Zero,
                };

                LinuxVulkanNative.ThrowIfFailed(
                    LinuxVulkanNative.CreateInstance(ref createInfo, IntPtr.Zero, out IntPtr instance),
                    "vkCreateInstance");

                if (instance == IntPtr.Zero)
                    throw new LinuxVulkanException("vkCreateInstance returned a null instance handle.");

                return instance;
            }
            finally
            {
                Marshal.FreeHGlobal(applicationInfoPtr);
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(applicationName);
            Marshal.FreeCoTaskMem(engineName);
        }
    }

    private static PhysicalDeviceSelection SelectPhysicalDevice(IntPtr instance)
    {
        uint physicalDeviceCount = 0;
        LinuxVulkanNative.ThrowIfFailed(
            LinuxVulkanNative.EnumeratePhysicalDevices(instance, ref physicalDeviceCount, null),
            "vkEnumeratePhysicalDevices(count)");

        if (physicalDeviceCount == 0)
            throw new LinuxVulkanException("No Vulkan physical devices were reported by the active ICD.");

        IntPtr[] physicalDevices = new IntPtr[physicalDeviceCount];
        LinuxVulkanNative.ThrowIfFailed(
            LinuxVulkanNative.EnumeratePhysicalDevices(instance, ref physicalDeviceCount, physicalDevices),
            "vkEnumeratePhysicalDevices(list)");

        for (int deviceIndex = 0; deviceIndex < physicalDevices.Length; deviceIndex++)
        {
            IntPtr physicalDevice = physicalDevices[deviceIndex];
            if (physicalDevice == IntPtr.Zero)
                continue;

            LinuxVulkanDeviceInfo deviceInfo = LinuxVulkanNative.GetPhysicalDeviceInfo(physicalDevice);
            if (TryFindGraphicsQueueFamily(physicalDevice, out uint queueFamilyIndex))
                return new PhysicalDeviceSelection(physicalDevice, queueFamilyIndex, deviceIndex, deviceInfo);
        }

        throw new LinuxVulkanException("No Vulkan physical device exposes a graphics-capable queue family.");
    }

    private static bool TryFindGraphicsQueueFamily(IntPtr physicalDevice, out uint queueFamilyIndex)
    {
        queueFamilyIndex = 0;
        uint queueFamilyCount = 0;
        LinuxVulkanNative.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, ref queueFamilyCount, null);
        if (queueFamilyCount == 0)
            return false;

        LinuxVulkanNative.VkQueueFamilyProperties[] properties = new LinuxVulkanNative.VkQueueFamilyProperties[queueFamilyCount];
        LinuxVulkanNative.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, ref queueFamilyCount, properties);

        for (int index = 0; index < properties.Length; index++)
        {
            LinuxVulkanNative.VkQueueFamilyProperties property = properties[index];
            if (property.QueueCount > 0 && (property.QueueFlags & LinuxVulkanNative.VK_QUEUE_GRAPHICS_BIT) != 0)
            {
                queueFamilyIndex = (uint)index;
                return true;
            }
        }

        return false;
    }

    private static IntPtr CreateDevice(IntPtr physicalDevice, uint queueFamilyIndex)
    {
        float[] queuePriorities = [1.0f];
        GCHandle priorityHandle = GCHandle.Alloc(queuePriorities, GCHandleType.Pinned);
        IntPtr queueCreateInfoPtr = IntPtr.Zero;
        try
        {
            var queueCreateInfo = new LinuxVulkanNative.VkDeviceQueueCreateInfo
            {
                SType = LinuxVulkanNative.VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO,
                PNext = IntPtr.Zero,
                Flags = 0,
                QueueFamilyIndex = queueFamilyIndex,
                QueueCount = 1,
                PQueuePriorities = priorityHandle.AddrOfPinnedObject(),
            };

            queueCreateInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<LinuxVulkanNative.VkDeviceQueueCreateInfo>());
            Marshal.StructureToPtr(queueCreateInfo, queueCreateInfoPtr, fDeleteOld: false);

            var deviceCreateInfo = new LinuxVulkanNative.VkDeviceCreateInfo
            {
                SType = LinuxVulkanNative.VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO,
                PNext = IntPtr.Zero,
                Flags = 0,
                QueueCreateInfoCount = 1,
                PQueueCreateInfos = queueCreateInfoPtr,
                EnabledLayerCount = 0,
                PpEnabledLayerNames = IntPtr.Zero,
                EnabledExtensionCount = 0,
                PpEnabledExtensionNames = IntPtr.Zero,
                PEnabledFeatures = IntPtr.Zero,
            };

            LinuxVulkanNative.ThrowIfFailed(
                LinuxVulkanNative.CreateDevice(physicalDevice, ref deviceCreateInfo, IntPtr.Zero, out IntPtr device),
                "vkCreateDevice");

            if (device == IntPtr.Zero)
                throw new LinuxVulkanException("vkCreateDevice returned a null device handle.");

            return device;
        }
        finally
        {
            if (queueCreateInfoPtr != IntPtr.Zero)
                Marshal.FreeHGlobal(queueCreateInfoPtr);

            priorityHandle.Free();
        }
    }

    private static string FormatVersion(uint version) => LinuxVulkanNative.FormatApiVersion(version);

    private readonly record struct PhysicalDeviceSelection(
        IntPtr PhysicalDevice,
        uint QueueFamilyIndex,
        int PhysicalDeviceIndex,
        LinuxVulkanDeviceInfo DeviceInfo);
}
