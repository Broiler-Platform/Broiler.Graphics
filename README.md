# Broiler.Graphics

[![CI](https://github.com/Broiler-Platform/Broiler.Graphics/actions/workflows/ci.yml/badge.svg)](https://github.com/Broiler-Platform/Broiler.Graphics/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/vpre/Broiler.Graphics.svg)](https://www.nuget.org/packages/Broiler.Graphics)
[![License: Apache 2.0](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](https://github.com/Broiler-Platform/Broiler.Graphics/blob/main/LICENSE)

Broiler.Graphics is the rendering component for Broiler. It owns a platform-neutral
managed core — `BBitmap`, `BCanvas`, deterministic CPU rasterization, text and font
handling, and the render-list pipeline — plus one presentation backend per platform.
Image *decoding* deliberately lives outside this component: the core depends only on the
`Broiler.Media.Image` abstraction, and the application supplies the concrete codecs.

Native API declarations live in `Broiler.Native.Windows`, `Broiler.Native.Linux`,
and `Broiler.Native.Android`. All component dependencies are distributed via [NuGet.org](https://www.nuget.org)
and managed centrally in `Directory.Packages.props`.

> **Preview release.** Public APIs and behaviour are not frozen and may change before `1.0`.
> The Windows backend uses native interop, and the rendering path is fed untrusted image data through
> codecs the application injects; both deserve explicit review before production or
> security-sensitive use. Substantial implementation work was AI-assisted, and
> human-review approval is revision-scoped — consult [HUMAN_REVIEW.md](HUMAN_REVIEW.md)
> for the reviewed revision and conditions before describing a checkout as approved. See
> the [roadmap](docs/roadmap.md) for what is still open.

## Installation

All Broiler packages are published to **[NuGet.org](https://www.nuget.org/packages/Broiler.Graphics)**.
Preview packages require an explicit prerelease flag:

```bash
# Platform-neutral core
dotnet add package Broiler.Graphics --prerelease
```

Add the backend package for the platform you present on — backends are separate packages,
and none is pulled in automatically:

```bash
# Windows (Direct2D / DirectWrite)
dotnet add package Broiler.Graphics.Windows --prerelease

# Linux (OpenGL / EGL)
dotnet add package Broiler.Graphics.Linux.OpenGL --prerelease

# Linux (Vulkan)
dotnet add package Broiler.Graphics.Linux.Vulkan --prerelease

# Android (EGL / OpenGL ES)
dotnet add package Broiler.Graphics.Android --prerelease

# Browser WebAssembly (Canvas 2D)
dotnet add package Broiler.Graphics.WebAssembly --prerelease
```

There is no meta-package: an application takes the core plus exactly the backends it presents with.

## Quick Start

### Basic Canvas Drawing (CPU Rasterizer)

`Broiler.Graphics` provides a fully managed, deterministic CPU rasterizer that works identically
on all platforms:

```csharp
using Broiler.Graphics.Color;
using Broiler.Graphics.Imaging;
using Broiler.Graphics.Rendering;
using System.Drawing;

// Create an RGBA bitmap
using var bitmap = new BBitmap(width: 800, height: 600);

// Open a drawing canvas
using (BCanvas canvas = bitmap.OpenCanvas())
{
    // Clear background
    canvas.FillRect(new RectangleF(0, 0, 800, 600), BColor.White);

    // Draw clipped and transformed geometry
    canvas.Save();
    canvas.PushClip(new RectangleF(50, 50, 700, 500));
    canvas.Translate(50, 50);

    // Draw colored rectangles
    canvas.FillRect(new RectangleF(0, 0, 200, 150), BColor.FromRgba(0x21, 0x96, 0xF3, 0xFF));
    canvas.StrokeRect(new RectangleF(0, 0, 200, 150), BColor.Black, strokeWidth: 2f);

    canvas.PopClip();
    canvas.Restore();
}
```

### Injecting Image Codecs

The core references the image *abstraction* only (`Broiler.Media.Image`), never a concrete
implementation. In your application composition root, register codecs from `Broiler.Media.Image.Managed`
or custom providers:

```csharp
using Broiler.Graphics.Imaging;
using Broiler.Media.Image.Codecs;

// Inject concrete codecs into the core catalog
BImageCodecs.Use(new MediaCodecCatalog(ManagedImageCodecs.CreateCodecs()));
```

### Windows Direct2D Presentation

On Windows, `Broiler.Graphics.Windows` provides hardware-accelerated Direct2D rendering and windowing:

```csharp
using Broiler.Graphics.Windows;

// Create a window with Direct2D presentation
using var window = new Direct2DWindow(new WindowOptions
{
    Title = "Broiler Graphics Application",
    Width = 1024,
    Height = 768
});

window.OnRender += (renderer, surface) =>
{
    // Render frames via Direct2D hardware acceleration
};

window.Run();
```

## Packages

| Package | Target | Role |
| --- | --- | --- |
| `Broiler.Graphics` | `net10.0` | Platform-neutral core: bitmaps, canvas, geometry, colour, deterministic CPU raster, text and fonts, render lists. Trimming- and AOT-friendly, fully safe code. |
| `Broiler.Graphics.Windows` | `net10.0-windows` | Direct2D/DirectWrite backend, window and input integration, and the HWND-backed video presentation target. |
| `Broiler.Graphics.Linux` | `net10.0` | Shared Linux runtime support: native library probing, dependency resolution, runtime diagnostics. |
| `Broiler.Graphics.Linux.OpenGL` | `net10.0` | OpenGL/EGL presentation over Mesa/EGL, with pbuffer and opt-in X11 window surfaces. |
| `Broiler.Graphics.Linux.Vulkan` | `net10.0` | Vulkan 1.2 loader/device path. Presentation is still CPU-present. |
| `Broiler.Graphics.Android` | `net10.0` | EGL / OpenGL ES presentation. Reaches Android through P/Invoke, so it needs no `Mono.Android` reference and no android workload. |
| `Broiler.Graphics.WebAssembly` | `net10.0` | Browser Canvas backend: a platform-neutral frame planner plus `JSImport`/`JSExport` interop gated with `[SupportedOSPlatform("browser")]`. |

Every package ships XML documentation for IntelliSense and a `.snupkg` symbol package, and is built
deterministically with SourceLink.

### Dependency Direction

```text
Broiler.Graphics.Windows       -> Broiler.Graphics -> Broiler.Media.Image   (abstraction only)
Broiler.Graphics.Windows       -> Broiler.Media.Video                       (declares the HWND video target)
Broiler.Graphics.Linux.OpenGL  -> Broiler.Graphics.Linux -> Broiler.Graphics
Broiler.Graphics.Linux.Vulkan  -> Broiler.Graphics.Linux -> Broiler.Graphics
Broiler.Graphics.Android       -> Broiler.Graphics
Broiler.Graphics.WebAssembly   -> Broiler.Graphics
```

## Backend Status

| Backend | Status |
| --- | --- |
| Windows Direct2D | Complete: render-list replay, DirectWrite text, window and input integration, HWND video target. |
| Linux OpenGL | Preview: GPU-native replay covers clear, opaque fill/stroke rectangles, and rectangular clips. Text, images, rounded rectangles, transforms, and translucent draws fall back to CPU-present rendering, where render lists are replayed through the managed renderer, uploaded to an OpenGL texture/FBO, and presented through an EGL pbuffer or opt-in X11 window surface. |
| Linux Vulkan | Early preview: creates a Vulkan 1.2 loader/device path when available and shares the CPU-present fallback; WSI/swapchain presentation and Vulkan command replay are in development. |
| Android | EGL / OpenGL ES presentation surfaces and renderer via P/Invoke. |
| WebAssembly | Frame planner validated against a CPU oracle; the browser Canvas interop runs on the `browser-wasm` runtime. |

The Linux demo is the composition root for graphics plus input. It can open the
OpenGL X11 preview window and wire Linux evdev keyboard/mouse providers while pausing
delivery when the X11 window loses focus. It also prints OS/runtime, display-server, OpenGL driver,
Vulkan device, and evdev diagnostics. See the [roadmap](docs/roadmap.md#linux-backends) for remaining Linux milestones.

## Repository Layout

```text
src/                     runtime assemblies, one directory per package
src/tests/               self-hosted test runner executables
src/demos/               Windows and Linux demo applications (never packaged)
eng/                     packaging scripts, test runners, and metadata
docs/                    roadmap, packaging, and CI/CD documentation
.github/workflows/       CI and release publishing pipelines
Broiler.Graphics.slnx    solution containing all libraries, tests, and demos
Directory.Packages.props central package dependency versions
NuGet.config             hermetic NuGet.org configuration
```

External dependencies (`Broiler.Media.*`, `Broiler.Input.*`, `Broiler.Native.*`) are managed centrally
in `Directory.Packages.props` and resolved directly from NuGet.org.

## Building and Testing

Prerequisites:
- .NET 10 SDK
- PowerShell 7 (or Windows PowerShell 5.1+)
- Node.js 24
- Bash (Git Bash on Windows)

Build the solution and run tests:

```bash
dotnet build Broiler.Graphics.slnx -c Release
bash ./eng/run-tests.sh Release
node --test eng/resolve-preview-version.test.mjs
```

The test script automatically executes the core, WebAssembly, and Android test suites,
and explicitly compiles and executes the platform-specific suite for the host operating system
(Windows Direct2D or Linux).

## Demos

Run the Windows demo:

```bash
dotnet run --project src/demos/Broiler.Graphics.Windows.Demo -c Debug
```

Run the Linux demo:

```bash
dotnet run --project src/demos/Broiler.Graphics.Linux.Demo -c Debug
```

The Linux demo accepts options after `--`: `--vulkan` selects the Vulkan path, `--window
--enable-evdev-input --interactive` opens the X11 preview window and wires evdev input,
and `--artifact-dir=<path>` writes diagnostics artifacts to a specified path.

## Packaging and Releases

Package all 7 shipping libraries locally:

```powershell
pwsh -File eng/pack.ps1
```

Verify consumer restores against NuGet.org:

```powershell
pwsh -File eng/verify-feed.ps1 -Target nuget
```

Releases are published exclusively to **NuGet.org** via GitHub Actions.
See [CI, packages, and releases](docs/packaging.md) for detailed release procedures and automation.

## Preview Status

This is preview software. The security and stability guidelines recorded in
[HUMAN_REVIEW.md](HUMAN_REVIEW.md) apply:

- The component is preview software and is neither fully optimized nor final.
- Public APIs and behaviour may change while development continues.
- Image decoding parses complex binary input and must be treated as security-sensitive.
  Do not decode untrusted input in security-sensitive environments without sandboxing,
  resource limits, fuzzing, and review.
- The Windows backend uses native Windows APIs and Direct2D/DirectWrite/DXGI/D3D interop.
  Correct and security-relevant usage of every involved API is not guaranteed by a
  first-preview review.
- No dedicated fuzzing campaign, SAST report, dependency scan, or independent security
  audit is recorded. This review is not a production security audit.

Broiler.Graphics is an independent Broiler component. It is not part of, maintained by,
or endorsed by HTML Renderer or Yantra JS.

## Documentation

- [CI, packages, and releases](docs/packaging.md)
- [Current roadmap](docs/roadmap.md)
- [Human-review record](HUMAN_REVIEW.md)

## License

Broiler.Graphics is licensed under the [Apache License 2.0](LICENSE). Third-party
material, if present, retains the license identified with that material.
