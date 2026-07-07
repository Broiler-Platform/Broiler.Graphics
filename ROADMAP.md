# Broiler.Graphics - Roadmap

A snapshot of what works today and what's planned next. This is a living document;
items move from **Planned** to **Done** as they land.

## Done

### Managed image codec (`Broiler.Graphics`, dependency-free)
- **PNG** - decode every colour type (grayscale, RGB, palette, gray+alpha, RGBA) at
  bit depths 1/2/4/8/16, non-interlaced and Adam7-interlaced, with `tRNS`; encode
  8-bit RGBA.
- **APNG** - decode with full frame compositing (blend SOURCE/OVER, dispose
  NONE/BACKGROUND/PREVIOUS); encode a frame sequence (`EncodeAnimation`).
- **BMP** - decode uncompressed 24/32bpp; encode 32bpp BGRA.
- **JPEG** - decode baseline **and** progressive (1/3-component, common subsamplings,
  restart markers); encode baseline 4:2:0 with optimal (per-image) Huffman tables.
- Animation API: `BImageSequence` / `BImageFrame`, `DecodeAnimation` / `EncodeAnimation`.
- CPU bitmap/raster canvas: `BBitmap` and `BCanvas` provide dependency-free RGBA
  pixel storage, encode/decode helpers, clips, opacity/blend layers, gradients,
  bitmap blits, path strokes, polygons, and glyph contour fills for deterministic
  off-screen rendering.

### Direct2D backend (`Broiler.Graphics.Windows`)
- Registers the managed codec on renderer construction (`UseManagedIfUnset`, non-clobbering).
- Image resource path: `CreateImage` decodes via `BImageCodec`, converts to D2D-native
  BGRA premultiplied, and stores it; `DrawImage` resolves the handle, lazily uploads an
  `ID2D1Bitmap` (`CreateBitmap`), and issues `DrawBitmap`.
- Native GPU bootstrap: `D3D11CreateDevice` with BGRA support (hardware first, WARP
  fallback), `IDXGIDevice`, DXGI factory, `ID2D1Factory1`/`ID2D1Device`, and a shared
  DirectWrite factory.
- Per-surface Direct2D path: composition or HWND swap chain, `ID2D1DeviceContext`,
  DXGI back-buffer target bitmap, `Resize`, `BeginDraw`/`EndDraw`, `Present`, DPI
  scaling, and device-lost/target-recreate error mapping.
- Command replay for fill/stroke rectangles, text, images, rectangular clips, and
  transform stacks.

## Planned

### 1. Inter-frame diffing for smaller APNG output
The APNG encoder currently writes every frame as a full-canvas `SOURCE`/`NONE` image -
correct and self-contained, but larger than necessary. Real encoders shrink output by
emitting only each frame's changed sub-region.

**Scope**
- Diff consecutive frames to the tightest dirty rectangle; emit that sub-region via
  `fcTL`/`fdAT` with `x/y_offset`, choosing `blend_op` (OVER when the diff is over an
  unchanged background) and `dispose_op` to minimise data.
- Optionally support `BACKGROUND`/`PREVIOUS` dispose to reuse regions across frames.
- Keep frame 0 a full image in `IDAT` (the still fallback) - already the case.
- Validation: round-trip equals the input frames exactly (lossless), output is no larger
  than the current full-frame encoding, and the result stays decodable by an independent
  decoder (e.g. System.Drawing for the default image; browsers/`libpng` for the animation).

### 2. Linux Mesa backends
Add Linux as the second operating system for `Broiler.Graphics` with Mesa-backed
OpenGL and Vulkan backends. The first milestone should use a CPU-present path
that reuses the existing managed renderer and uploads the result to OpenGL or
Vulkan; GPU-native render-command replay is landing incrementally after the
CPU-present path proves parity.

**Phase 1 status:** scaffolded. `Broiler.Graphics.Linux`,
`Broiler.Graphics.Linux.OpenGL`, `Broiler.Graphics.Linux.Vulkan`,
`Broiler.Graphics.Linux.Tests`, and `Broiler.Graphics.Linux.Demo` now exist with
native dependency probes and placeholder renderer diagnostics.

**Phase 3 status:** OpenGL CPU-present preview landed. `LinuxOpenGlRenderer`
uses `BImageRenderer` for deterministic command replay, uploads the resulting
RGBA frame to an EGL/OpenGL texture/FBO when available, supports pbuffer
readback, and exposes an opt-in X11/EGL window surface for Linux desktop smoke
testing.

**Phase 4 status:** Vulkan loader/device preview landed. `LinuxVulkanRenderer`
uses the same deterministic CPU render-list replay, creates a Vulkan 1.2
instance/logical device and graphics queue on Linux when available, exposes
backend diagnostics, and lets the Linux demo switch to `--vulkan`. Vulkan
WSI/swapchain creation, resize handling, and image upload/present remain the
next Phase 4 slice.

**Phase 5 status:** Linux demo integration preview landed. The demo now composes
graphics and input at the application boundary: `--window --enable-evdev-input`
opens the X11/OpenGL preview, discovers one readable keyboard and mouse through
the Linux evdev providers, routes typed Input events into demo state, and starts
or stops raw event-device reads when the X11 window gains or loses focus. Vulkan
input/window integration remains blocked on Vulkan WSI/swapchain presentation.

**Phase 6 status:** OpenGL GPU-native command replay first slice landed. The
native replay planner accepts clear, opaque fill/stroke rectangles, and
rectangular clips, then executes the replay through OpenGL scissor rectangles and
clears. Unsupported commands, including translucent draws, rounded rectangles,
images, text, and transforms, fall back to CPU-present rendering. The Linux demo
prints native-replay diagnostics and can save backend/CPU comparison PNGs with
`--artifact-dir=...`. Vulkan remains CPU-present while WSI/swapchain presentation
and Vulkan command replay are still pending.

**Phase 7 status:** Linux hardening first slice landed. The demo now prints
OS/runtime/display diagnostics before backend probes, OpenGL diagnostics include
vendor/renderer/GL/GLSL strings when a context exists, Vulkan diagnostics include
selected physical-device identity and API/driver versions, and input reporting
shows sanitized selected evdev devices. The preview hardening notes document
distro package starting points, evdev permission caveats, validation commands,
and the hardware/software matrix still required before a shippable preview.

See [Linux second-OS roadmap](../docs/roadmap/linux-graphics-input-roadmap.md).

## Ideas / not scheduled
- WIC-free animated **GIF** decode/encode.
- PNG/JPEG **encoder** tuning (PNG filter heuristics per-row already; add palette/indexed
  output; JPEG restart-interval and subsampling options).
- Wire the codec into additional backends as they appear.
