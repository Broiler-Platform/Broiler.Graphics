using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Broiler.Graphics.Linux;
using Broiler.Graphics.Linux.OpenGL;
using Broiler.Graphics.Linux.Vulkan;

namespace Broiler.Graphics.Linux.Demo;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.WriteLine("Broiler.Graphics Linux Phase 7 hardening preview");
        Console.WriteLine();
        PrintRuntime(LinuxGraphicsRuntimeDiagnostics.Capture());
        Print("Baseline", LinuxGraphicsDependencies.CheckWindowingBaseline());
        Print("OpenGL", LinuxOpenGlRenderer.CheckDependencies());
        Print("Vulkan", LinuxVulkanRenderer.CheckDependencies());

        LinuxDemoOptions options = LinuxDemoOptions.Parse(args);
        using CancellationTokenSource shutdown = new();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.Cancel();
        };

        if (options.Backend == GraphicsBackend.Vulkan)
            await RunVulkanSmoke(options, shutdown.Token).ConfigureAwait(false);
        else
            await RunOpenGlSmoke(options, shutdown.Token).ConfigureAwait(false);

        return 0;
    }

    private static async Task RunOpenGlSmoke(LinuxDemoOptions options, CancellationToken cancellationToken)
    {
        using LinuxOpenGlRenderer renderer = new();
        BSurfaceDescriptor descriptor = BSurfaceDescriptor.Default(new BSize(320, 240));
        using IBroilerSurface surface = options.OpenWindow
            ? renderer.CreateX11WindowSurface(descriptor, "Broiler.Graphics Linux Phase 7")
            : renderer.CreateSurface(new BSurfaceDescriptor(new BSize(32, 24), 1.0));

        await RunRenderLoopAsync(renderer, surface, options, cancellationToken).ConfigureAwait(false);
    }

    private static async Task RunVulkanSmoke(LinuxDemoOptions options, CancellationToken cancellationToken)
    {
        if (options.OpenWindow)
            Console.WriteLine("Vulkan --window requested; WSI/swapchain presentation is still pending, so running offscreen.");
        if (options.EnableEvdevInput)
            Console.WriteLine("evdev input requires a focus-capable X11 window in this preview; input is disabled for Vulkan offscreen mode.");

        using LinuxVulkanRenderer renderer = new();
        using IBroilerSurface surface = renderer.CreateSurface(new BSurfaceDescriptor(new BSize(32, 24), 1.0));
        await RunRenderLoopAsync(renderer, surface, options with { OpenWindow = false, EnableEvdevInput = false }, cancellationToken).ConfigureAwait(false);
    }

    private static async Task RunRenderLoopAsync(
        IBroilerRenderer renderer,
        IBroilerSurface surface,
        LinuxDemoOptions options,
        CancellationToken cancellationToken)
    {
        LinuxOpenGlX11WindowSurface? x11Window = surface as LinuxOpenGlX11WindowSurface;
        bool canUseEvdev = options.EnableEvdevInput && x11Window is not null;
        if (options.EnableEvdevInput && x11Window is null)
            Console.WriteLine("evdev input requested, but no focus-capable X11 window exists; input is disabled.");

        await using LinuxDemoInputCoordinator input = new(canUseEvdev, Console.WriteLine);
        await input.InitializeAsync(cancellationToken).ConfigureAwait(false);

        DateTimeOffset start = DateTimeOffset.UtcNow;
        int frameIndex = 0;
        long renderTicks = 0;
        BRenderList? lastList = null;
        BFrameContext lastFrameContext = new(BColor.Transparent);
        using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(options.OpenWindow ? 16 : 1));
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (x11Window is not null)
            {
                x11Window.ProcessPendingEvents();
                await input.SetActiveAsync(x11Window.IsFocused, cancellationToken).ConfigureAwait(false);
            }

            BRenderList list = CreateSmokeRenderList(surface, input.Snapshot, frameIndex++);
            BFrameContext frameContext = new(BColor.Transparent, frameIndex, BRenderOptions.Default);
            Stopwatch stopwatch = Stopwatch.StartNew();
            renderer.Render(surface, list, frameContext);
            stopwatch.Stop();
            renderTicks += stopwatch.ElapsedTicks;
            lastList = list;
            lastFrameContext = frameContext;
            if (!options.OpenWindow)
                break;

            if (!options.RunUntilClose &&
                (DateTimeOffset.UtcNow - start).TotalMilliseconds >= options.DurationMilliseconds)
            {
                break;
            }
        }
        while (!input.QuitRequested &&
               (x11Window is null || !x11Window.IsCloseRequested) &&
               await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));

        await input.SetActiveAsync(false, cancellationToken).ConfigureAwait(false);
        using BBitmap bitmap = ReadSurface(surface);
        Console.WriteLine(PresentationLabel(surface) + " smoke:");
        Console.WriteLine($"  presentation: {PresentationState(surface)}");
        Console.WriteLine($"  diagnostic: {SurfaceDiagnostic(surface)}");
        Console.WriteLine($"  bitmap: {bitmap.Width}x{bitmap.Height}");
        Console.WriteLine($"  sample(5,5): {bitmap.GetPixel(5, 5)}");
        Console.WriteLine($"  render-time: {FormatMilliseconds(renderTicks)} ms total across {frameIndex} frame(s)");
        PrintNativeReplayInspection(surface, lastList, lastFrameContext);
        Console.WriteLine($"  input: {InputSummary(input.Snapshot)}");
        SaveArtifacts(options, surface, bitmap, lastList, lastFrameContext);
        Console.WriteLine();
    }

    private static BRenderList CreateSmokeRenderList(
        IBroilerSurface surface,
        LinuxDemoInputSnapshot input,
        int frameIndex)
    {
        var list = new BRenderList();
        list.FillRect(new BRect(0, 0, surface.Size.Width, surface.Size.Height), BColor.White);
        list.FillRect(new BRect(4, 4, surface.Size.Width / 3.0, surface.Size.Height / 3.0), AccentColor(input.AccentIndex));
        list.StrokeRect(new BRect(2, 2, surface.Size.Width - 4, surface.Size.Height - 4), BColor.Blue, 1);

        if (input.Enabled)
            AddInputOverlay(list, surface.Size, input, frameIndex);

        return list;
    }

    private static void AddInputOverlay(
        BRenderList list,
        BSize size,
        LinuxDemoInputSnapshot input,
        int frameIndex)
    {
        BColor focusColor = input.Active ? BColor.Green : BColor.FromArgb(255, 128, 128, 128);
        list.FillRect(new BRect(6, size.Height - 10, input.Active ? 18 : 8, 4), focusColor);

        double centerX = size.Width / 2.0;
        double centerY = size.Height / 2.0;
        double pointerX = Clamp(centerX + input.PointerX, 4, Math.Max(4, size.Width - 4));
        double pointerY = Clamp(centerY + input.PointerY, 4, Math.Max(4, size.Height - 4));
        list.FillRect(new BRect(pointerX - 2, pointerY - 2, 4, 4), BColor.Black);

        int pulse = (input.KeyEvents + input.MouseButtonEvents + input.MouseWheelEvents + frameIndex) % 24;
        list.FillRect(new BRect(size.Width - 8 - pulse, size.Height - 9, pulse, 3), AccentColor(input.AccentIndex + 1));
    }

    private static BBitmap ReadSurface(IBroilerSurface surface) =>
        surface switch
        {
            LinuxOpenGlSurface openGlSurface => openGlSurface.ReadToBitmap(),
            LinuxOpenGlX11WindowSurface x11Surface => x11Surface.ReadToBitmap(),
            LinuxVulkanSurface vulkanSurface => vulkanSurface.ReadToBitmap(),
            _ => throw new InvalidOperationException("Unexpected Linux graphics surface."),
        };

    private static string PresentationLabel(IBroilerSurface surface) =>
        surface switch
        {
            LinuxOpenGlSurface or LinuxOpenGlX11WindowSurface => "OpenGL",
            LinuxVulkanSurface => "Vulkan",
            _ => "Linux graphics",
        };

    private static string PresentationState(IBroilerSurface surface) =>
        surface switch
        {
            LinuxOpenGlSurface openGlSurface => openGlSurface.IsGpuBacked ? "EGL/OpenGL pbuffer" : "CPU fallback",
            LinuxOpenGlX11WindowSurface x11Surface => x11Surface.IsGpuBacked ? "X11/EGL/OpenGL window" : "CPU fallback",
            LinuxVulkanSurface vulkanSurface => vulkanSurface.IsDeviceBacked ? "Vulkan loader/device" : "CPU fallback",
            _ => "unknown",
        };

    private static string SurfaceDiagnostic(IBroilerSurface surface) =>
        surface switch
        {
            LinuxOpenGlSurface openGlSurface => openGlSurface.Diagnostic,
            LinuxOpenGlX11WindowSurface x11Surface => x11Surface.Diagnostic,
            LinuxVulkanSurface vulkanSurface => vulkanSurface.Diagnostic,
            _ => string.Empty,
        };

    private static void PrintNativeReplayInspection(
        IBroilerSurface surface,
        BRenderList? renderList,
        BFrameContext frameContext)
    {
        if (!IsOpenGlSurface(surface) || renderList is null)
            return;

        LinuxOpenGlNativeReplayInspection inspection = LinuxOpenGlNativeReplay.Inspect(renderList, ToDescriptor(surface), frameContext);
        string state = inspection.IsSupported ? "supported" : "fallback";
        Console.WriteLine($"  native-replay: {state} - {inspection.Diagnostic}");
    }

    private static void SaveArtifacts(
        LinuxDemoOptions options,
        IBroilerSurface surface,
        BBitmap backendBitmap,
        BRenderList? renderList,
        BFrameContext frameContext)
    {
        if (options.ArtifactDirectory is null || renderList is null)
            return;

        Directory.CreateDirectory(options.ArtifactDirectory);
        string backend = options.Backend == GraphicsBackend.Vulkan ? "vulkan" : "opengl";
        string backendPath = Path.Combine(options.ArtifactDirectory, $"broiler-linux-{backend}-phase7.png");
        backendBitmap.Save(backendPath);

        using BImageRenderer cpu = new();
        using BBitmap cpuBitmap = cpu.RenderToImage(renderList, ToDescriptor(surface), frameContext);
        string cpuPath = Path.Combine(options.ArtifactDirectory, "broiler-linux-cpu-phase7.png");
        cpuBitmap.Save(cpuPath);
        Console.WriteLine($"  artifacts: {cpuPath}; {backendPath}");
    }

    private static BSurfaceDescriptor ToDescriptor(IBroilerSurface surface) =>
        new(surface.Size, surface.DpiScale);

    private static bool IsOpenGlSurface(IBroilerSurface surface) =>
        surface is LinuxOpenGlSurface or LinuxOpenGlX11WindowSurface;

    private static string InputSummary(LinuxDemoInputSnapshot input)
    {
        if (!input.Enabled)
            return "disabled";

        if (!input.Initialized)
            return "requested but not opened";

        string keyboard = input.KeyboardDevice ?? "none";
        string mouse = input.MouseDevice ?? "none";
        return $"active={input.Active}, keyboard={keyboard}, mouse={mouse}, keys={input.KeyEvents}, moves={input.MouseMoveEvents}, buttons={input.MouseButtonEvents}, wheels={input.MouseWheelEvents}";
    }

    private static BColor AccentColor(int index)
    {
        int value = ((index % 4) + 4) % 4;
        return value switch
        {
            1 => BColor.Green,
            2 => BColor.Blue,
            3 => BColor.FromArgb(255, 255, 128, 0),
            _ => BColor.Red,
        };
    }

    private static double Clamp(double value, double min, double max) =>
        value < min ? min : value > max ? max : value;

    private static string FormatMilliseconds(long ticks) =>
        (ticks * 1000.0 / Stopwatch.Frequency).ToString("0.###", CultureInfo.InvariantCulture);

    private static void PrintRuntime(LinuxGraphicsRuntimeReport report)
    {
        Console.WriteLine("Runtime:");
        Console.WriteLine($"  os: {report.OperatingSystemDescription}");
        Console.WriteLine($"  framework: {report.FrameworkDescription}");
        Console.WriteLine($"  arch: {report.ProcessArchitecture}");
        Console.WriteLine($"  rid: {report.RuntimeIdentifier}");
        Console.WriteLine($"  display: {report.DisplayServer.Diagnostic}");
        Console.WriteLine();
    }

    private static void Print(string title, System.Collections.Generic.IReadOnlyList<LinuxNativeLibraryStatus> statuses)
    {
        Console.WriteLine(title + ":");
        foreach (LinuxNativeLibraryStatus status in statuses)
        {
            string state = status.IsAvailable ? "available" : "missing";
            Console.WriteLine($"  {status.Id}: {state} - {status.Diagnostic}");
        }

        Console.WriteLine();
    }
}
