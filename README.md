# Broiler.Graphics

A .NET 10 graphics component with a platform-neutral managed core, a Windows Direct2D
backend, and Linux OpenGL/Vulkan backend work.

The core includes `BBitmap`, `BCanvas`, deterministic CPU raster operations, and
managed PNG/APNG, BMP, and JPEG codecs. The Windows assembly provides the Direct2D
backend, window/input integration, and demo application. The Linux OpenGL assembly
provides a Mesa/EGL path with two preview modes: a first GPU-native OpenGL replay
slice for clear, opaque fill/stroke rectangles, and rectangular clips, plus a
CPU-present fallback where render lists are replayed through the managed renderer,
uploaded to an OpenGL texture/FBO, and presented through an EGL pbuffer or opt-in
X11 window surface. Unsupported commands such as text, images, rounded rectangles,
transforms, and translucent draws fall back to CPU-present rendering. The Linux
Vulkan assembly now creates a Vulkan 1.2 loader/device path when available and
shares the CPU-present render fallback while WSI/swapchain presentation and Vulkan
command replay are still being built out. See [ROADMAP.md](ROADMAP.md) for current
implementation status.

The Linux demo is the current composition root for graphics plus input. It can
open the OpenGL X11 preview window and, with explicit raw-input acknowledgement,
wire Linux evdev keyboard/mouse providers while pausing delivery when the X11
window loses focus. The Phase 7 hardening preview also prints OS/runtime,
display-server, OpenGL driver, Vulkan device, and selected evdev-device
diagnostics. See [Linux preview hardening notes](../docs/roadmap/linux-preview-hardening.md).

## Preview status

This is first-preview software. APIs and behavior may change without compatibility
guarantees. The Windows backend uses native interop, and image decoders process untrusted
binary input; both deserve explicit review before production or security-sensitive use.
Substantial implementation work was AI-assisted. The component is **not human-approved
for preview use** while [HUMAN_REVIEW.md](HUMAN_REVIEW.md) remains `PENDING`.

Broiler.Graphics is an independent Broiler component. It is not part of, maintained by,
or endorsed by HTML Renderer or Yantra JS.

## Build, test, and demo

```powershell
dotnet build Broiler.Graphics.sln
dotnet test Broiler.Graphics.sln
dotnet run --project Broiler.Graphics.Windows.Demo\Broiler.Graphics.Windows.Demo.csproj
dotnet run --project Broiler.Graphics.Linux.Demo\Broiler.Graphics.Linux.Demo.csproj
dotnet run --project Broiler.Graphics.Linux.Demo\Broiler.Graphics.Linux.Demo.csproj -- --vulkan
dotnet run --project Broiler.Graphics.Linux.Demo\Broiler.Graphics.Linux.Demo.csproj -- --window --enable-evdev-input --interactive
dotnet run --project Broiler.Graphics.Linux.Demo\Broiler.Graphics.Linux.Demo.csproj -- --artifact-dir="$env:TEMP\broiler-phase7"
```

## License

Broiler.Graphics is licensed under the [Apache License 2.0](LICENSE). Third-party
material, if present, retains the license identified with that material. The license
provides the software on an “AS IS” basis, without warranties or conditions.
