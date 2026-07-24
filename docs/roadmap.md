# Broiler.Graphics roadmap

This document lists only unfinished work. Implemented codec, raster, Direct2D, Linux
preview, and browser-Canvas milestones are represented by the code and tests rather
than repeated as history here.

## Image codecs

### APNG inter-frame optimization

`EncodeAnimation` currently emits full-canvas `SOURCE`/`NONE` frames. Reduce encoded
size without changing decoded pixels:

1. compute the tight dirty rectangle between consecutive frames;
2. emit correct `fcTL` offsets and cropped `fdAT` payloads;
3. choose `SOURCE`/`OVER` and, where useful, `BACKGROUND`/`PREVIOUS`;
4. keep frame zero as the complete `IDAT` fallback; and
5. validate lossless round trips plus decoding with an independent implementation.

The optimized result must never be larger than the existing full-frame encoding for the
same input; otherwise the encoder must keep the current representation.

## Linux backends

The OpenGL CPU-present path, initial native replay, Vulkan device path, X11 preview, and
diagnostics exist. The remaining work is:

- implement Vulkan WSI/swapchain creation, resize, image upload, and presentation;
- add Vulkan-native command replay with an explicit CPU fallback;
- extend OpenGL-native replay beyond opaque rectangles and rectangular clips to the
  command families justified by measurements (images, text, rounded geometry,
  transforms, and translucent drawing);
- add a Wayland presentation path only after its ownership and input/text boundaries are
  explicit; and
- complete the hardware/software matrix on x64 and Arm64 with llvmpipe/lavapipe and
  representative Mesa drivers.

Exit gate:

- backend/CPU artifact comparisons stay within documented tolerances;
- unsupported commands always take a deterministic fallback;
- window resize and device-loss paths recover without stale resources;
- diagnostics identify the selected driver/device without leaking sensitive device
  paths; and
- the preview matrix records distro, display server, GPU/driver, architecture, and
  result.

Raw Linux keyboard and mouse handling is owned by Broiler.Input. This repository should
consume its neutral events at the demo/application boundary rather than duplicate evdev
policy.

## Browser WebAssembly backend

The direct Canvas 2D planner, browser replay module, CPU fallback, and headless
conformance tests are implemented. Production-support evidence is still open:

- run committed Chromium, Firefox, and WebKit browser tests, including AOT publish;
- compare CPU and Canvas artifacts with explicit text/antialias tolerances;
- measure frame time, input-to-present latency, memory, and a ten-minute soak on
  reference hardware;
- verify text measurement, caret, and selection geometry within one CSS pixel for the
  claimed font set; and
- document the intentionally ignored or reinterpreted render options and opaque-canvas
  limitation in public API guidance.

UI scheduling, input, clipboard, accessibility, and application-port work belong to
Broiler.UI and the browser host. Skia compatibility removal belongs to Broiler.HTML;
Broiler.Graphics has no Skia package to remove.

## Release hardening

Before claiming a new preview revision:

- rerun managed codec fuzzing and malformed-input limits;
- run Windows native-backend lifecycle/device-loss tests;
- complete the Linux and browser matrices above; and
- update `HUMAN_REVIEW.md` for the exact revision and scope.
