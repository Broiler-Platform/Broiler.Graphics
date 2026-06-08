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

## Ideas / not scheduled
- WIC-free animated **GIF** decode/encode.
- PNG/JPEG **encoder** tuning (PNG filter heuristics per-row already; add palette/indexed
  output; JPEG restart-interval and subsampling options).
- Wire the codec into additional backends as they appear.
