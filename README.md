# Broiler.Graphics

[![License: Apache 2.0](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](https://github.com/Broiler-Platform/Broiler.Graphics/blob/main/LICENSE)

Broiler.Graphics is the rendering component for Broiler. It owns a platform-neutral
managed core — `BBitmap`, `BCanvas`, deterministic CPU rasterization, text and font
handling, and the render-list pipeline — plus one presentation backend per platform.
Image *decoding* deliberately lives outside this component: the core depends only on the
`Broiler.Media.Image` abstraction, and the application supplies the concrete codecs.

> **Preview release.** `0.1.0-preview.1` is the first published preview. Public APIs and
> behaviour are not frozen and may change before `1.0`. The Windows backend uses native
> interop, and the rendering path is fed untrusted image data through codecs the
> application injects; both deserve explicit review before production or
> security-sensitive use. Substantial implementation work was AI-assisted, and
> human-review approval is revision-scoped — consult [HUMAN_REVIEW.md](HUMAN_REVIEW.md)
> for the reviewed revision and conditions before describing a checkout as approved. See
> the [roadmap](docs/roadmap.md) for what is still open.

## Installation

Preview packages need an explicit prerelease opt-in:

```bash
dotnet add package Broiler.Graphics --prerelease
```

`Broiler.Graphics` is the platform-neutral core and rasterizes on the CPU on its own. Add
the backend package for the platform you present on — they are separate packages, and
none is pulled in automatically:

```bash
dotnet add package Broiler.Graphics.Windows --prerelease
```

There is no meta-package: an application takes the core plus exactly the backends it
presents with.

### Consuming Broiler packages from GitHub Packages

`NuGet.config` in the repository root pins two sources — nuget.org and the
Broiler-Platform GitHub Packages feed — and clears whatever the machine has configured,
so a restore resolves identically everywhere. Package source mapping sends `Broiler.*` to
either feed and everything else to nuget.org only.

That mapping is load-bearing. GitHub Packages requires authentication **even for public
packages** and answers `401` to an anonymous request, so an unmapped source would be
queried for every package and break the restore. Because this repository takes its
Broiler dependencies through the submodules as project references, nothing queries that
feed today and no credentials are needed to build.

To actually pull `Broiler.*` from GitHub Packages you need a personal access token with
the `read:packages` scope. Put it in your **user-level** config, never in the committed
one:

```bash
dotnet nuget update source broiler-github --username <github-user> --password <pat> --store-password-in-clear-text --configfile "$APPDATA/NuGet/NuGet.Config"
```

In GitHub Actions use `secrets.GITHUB_TOKEN` rather than a personal token.

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

Every package ships XML documentation and a `.snupkg` symbol package, and is built
deterministically with SourceLink.

### Dependency direction

```text
Broiler.Graphics.Windows       -> Broiler.Graphics -> Broiler.Media.Image   (abstraction only)
Broiler.Graphics.Windows       -> Broiler.Media.Video                       (declares the HWND video target)
Broiler.Graphics.Linux.OpenGL  -> Broiler.Graphics.Linux -> Broiler.Graphics
Broiler.Graphics.Linux.Vulkan  -> Broiler.Graphics.Linux -> Broiler.Graphics
Broiler.Graphics.Android       -> Broiler.Graphics
Broiler.Graphics.WebAssembly   -> Broiler.Graphics
```

The core references the image *abstraction* only, never an implementation. The
application composition root supplies the concrete codecs:

```csharp
BImageCodecs.Use(new MediaCodecCatalog(ManagedImageCodecs.CreateCodecs()));
```

## Backend status

| Backend | Status |
| --- | --- |
| Windows Direct2D | The most complete backend: render-list replay, DirectWrite text, window and input integration. |
| Linux OpenGL | Preview. A GPU-native replay slice covers clear, opaque fill/stroke rectangles, and rectangular clips; text, images, rounded rectangles, transforms, and translucent draws fall back to CPU-present rendering, where render lists are replayed through the managed renderer, uploaded to an OpenGL texture/FBO, and presented through an EGL pbuffer or opt-in X11 window surface. |
| Linux Vulkan | Earliest preview. Creates a Vulkan 1.2 loader/device path when available and shares the CPU-present fallback; WSI/swapchain presentation and Vulkan command replay are still being built out. |
| Android | EGL / OpenGL ES presentation surfaces and renderer. |
| WebAssembly | Frame planner validated against a CPU oracle; the browser Canvas interop runs on the `browser-wasm` runtime. |

The Linux demo is the current composition root for graphics plus input. It can open the
OpenGL X11 preview window and, with explicit raw-input acknowledgement, wire Linux evdev
keyboard/mouse providers while pausing delivery when the X11 window loses focus. It also
prints OS/runtime, display-server, OpenGL driver, Vulkan device, and selected
evdev-device diagnostics. The [roadmap](docs/roadmap.md#linux-backends) holds the
remaining Linux validation and implementation work.

## Repository layout

```text
src/                     runtime assemblies, one directory per package
src/tests/               one self-hosted test runner executable per assembly
src/demos/               Windows and Linux demo applications (never packaged)
eng/                     vendored packaging metadata and package icon
docs/                    roadmap
Broiler.Media/           submodule; supplies the image and video abstractions
Broiler.Input/           submodule; keyboard and mouse providers for the Linux demo
Broiler.Graphics.slnx    solution over every project in src/
```

Cross-component dependencies are git submodules at the repository root, and between them
they close the graph — every project reference resolves inside a checkout of this
repository.

`Broiler.Media` supplies `Broiler.Media.Image` to the core and `Broiler.Media.Video` to
the Windows backend, and its managed codecs act as the composition root for the test
runners. `Broiler.Input` supplies the evdev keyboard and mouse providers, and only the
Linux demo references it — no package depends on it.

Broiler.Media in turn declares Broiler.Graphics as a submodule of its own, but only its
`-Windows` configuration needs it. Initialise one level, as below, and the cycle stops.

## Building and testing

Clone with submodules, or initialise them in an existing checkout:

```bash
git clone --recurse-submodules https://github.com/Broiler-Platform/Broiler.Graphics.git
```

```bash
git submodule update --init
```

The solution defines six configurations. `Debug`/`Release` build the platform-neutral set
— core, WebAssembly, Android, and their test runners. The `-Windows` and `-Linux`
variants add the backends for that platform and select the matching runtime identifier.

```bash
dotnet build Broiler.Graphics.slnx -c Release-Windows
```

Platform projects build **only** under their own configuration: `Broiler.Graphics.Windows`,
its test runner, and the Windows demo are excluded from every configuration except
`Debug-Windows`/`Release-Windows`, and the three Linux projects, the Linux test runner,
and the Linux demo likewise build only under `Debug-Linux`/`Release-Linux`. A plain
`dotnet build` therefore does not compile the Direct2D backend — pass `-c Debug-Windows`
when that is what you mean to check.

Tests are self-hosted console runners rather than a test framework, so there is nothing
for `dotnet test` to discover. Run each suite the configuration produced:

```bash
dotnet run --project src/tests/Broiler.Graphics.Tests -c Debug
```

```bash
dotnet run --project src/tests/Broiler.Graphics.WebAssembly.Tests -c Debug
```

```bash
dotnet run --project src/tests/Broiler.Graphics.Android.Tests -c Debug
```

```bash
dotnet run --project src/tests/Broiler.Graphics.Windows.Tests -c Debug-Windows
```

```bash
dotnet run --project src/tests/Broiler.Graphics.Linux.Tests -c Debug-Linux
```

## Demos

```bash
dotnet run --project src/demos/Broiler.Graphics.Windows.Demo -c Debug-Windows
```

```bash
dotnet run --project src/demos/Broiler.Graphics.Linux.Demo -c Debug-Linux
```

The Linux demo takes options after `--`: `--vulkan` selects the Vulkan path, `--window
--enable-evdev-input --interactive` opens the X11 preview window and wires evdev input,
and `--artifact-dir=<path>` writes the diagnostics artifacts somewhere specific.

```bash
dotnet run --project src/demos/Broiler.Graphics.Linux.Demo -c Debug-Linux -- --window --enable-evdev-input --interactive
```

## Packaging

Each configuration packs the projects it builds, so the full package set takes three runs
into one output directory:

```bash
dotnet pack Broiler.Graphics.slnx -c Release -o ./artifacts
```

```bash
dotnet pack Broiler.Graphics.slnx -c Release-Windows -o ./artifacts
```

```bash
dotnet pack Broiler.Graphics.slnx -c Release-Linux -o ./artifacts
```

Test and demo projects never pack. `eng/Broiler.Packaging.props` is a vendored copy of
the suite-wide packaging metadata and holds the version, which stays in lockstep across
Broiler components during preview — edit the canonical file and re-run the sync script
rather than editing the copy.

## Preview status

This is first-preview software, and the security and stability warnings recorded in
[HUMAN_REVIEW.md](HUMAN_REVIEW.md) apply to any published preview:

- The component is preview software and is neither fully optimized nor final.
- Public APIs and behaviour may change while the global refactoring continues.
- Image decoding is injected from `Broiler.Media`, parses complex binary input, and must
  be treated as security-sensitive. Do not decode untrusted input in security-sensitive
  environments without sandboxing, resource limits, fuzzing, and additional review.
- The Windows backend uses native Windows APIs and Direct2D/DirectWrite/DXGI/D3D interop.
  Correct and security-relevant usage of every involved API is not guaranteed by a
  first-preview review.
- No dedicated fuzzing campaign, SAST report, dependency scan, or independent security
  audit is recorded. This review is not a production security audit.

Broiler.Graphics is an independent Broiler component. It is not part of, maintained by,
or endorsed by HTML Renderer or Yantra JS.

## Documentation

- [Current roadmap](docs/roadmap.md)
- [Human-review record](HUMAN_REVIEW.md)

## License

Broiler.Graphics is licensed under the [Apache License 2.0](LICENSE). Third-party
material, if present, retains the license identified with that material. The license
provides the software on an "AS IS" basis, without warranties or conditions.
