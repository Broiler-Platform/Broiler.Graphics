using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace Broiler.Graphics.WebAssembly;

/// <summary>
/// The narrow JavaScript boundary for the direct-Canvas backend. Per the roadmap the
/// boundary is: one module load, one canvas bind, one batched present per frame, image
/// resource upload/release, and explicit disposal. Every numeric value crossing into
/// managed code is validated by the JavaScript module before it is handed back.
/// </summary>
[SupportedOSPlatform("browser")]
internal static partial class CanvasInterop
{
    internal const string ModuleName = "broiler.graphics.webassembly.js";

    /// <summary>Imports the replay module from a host-provided URL. Must be awaited before any other call.</summary>
    internal static Task LoadModuleAsync(string moduleUrl) => JSHost.ImportAsync(ModuleName, moduleUrl);

    /// <summary>Binds the backend to a canvas element (by CSS selector) and acquires its 2D context.</summary>
    [JSImport("initialize", ModuleName)]
    internal static partial void Initialize(string canvasSelector);

    /// <summary>
    /// Replays one planned frame in a single call: sizes the canvas backing/CSS box, clears,
    /// and executes the batched op stream. <paramref name="strings"/> carries text runs referenced
    /// by <c>DrawText</c> ops.
    /// </summary>
    [JSImport("presentFrame", ModuleName)]
    internal static partial void PresentFrame(
        double[] stream,
        int streamLength,
        string[] strings,
        int backingWidth,
        int backingHeight,
        double clearColorArgb,
        double cssWidth,
        double cssHeight,
        int opCount);

    /// <summary>Whole-frame CPU fallback: uploads a straight-alpha RGBA frame through putImageData.</summary>
    [JSImport("presentImageData", ModuleName)]
    internal static partial void PresentImageData(
        int backingWidth,
        int backingHeight,
        byte[] rgba,
        double cssWidth,
        double cssHeight);

    /// <summary>Uploads a decoded RGBA image as a reusable resource canvas keyed by <paramref name="id"/>.</summary>
    [JSImport("uploadImage", ModuleName)]
    internal static partial void UploadImage(double id, int width, int height, byte[] rgba);

    /// <summary>Releases a resource canvas previously uploaded by <see cref="UploadImage"/>.</summary>
    [JSImport("releaseImage", ModuleName)]
    internal static partial void ReleaseImage(double id);

    /// <summary>Tears down the canvas binding and releases all resource canvases and observers.</summary>
    [JSImport("dispose", ModuleName)]
    internal static partial void Dispose();
}
