# Broiler.Graphics

A platform-neutral graphics core with a dependency-free managed image codec
(PNG/APNG, BMP, JPEG) and a Direct2D backend.

The core assembly also includes `BBitmap` and `BCanvas`, a dependency-free RGBA
bitmap plus CPU raster canvas for deterministic off-screen rendering and tests.

See [ROADMAP.md](ROADMAP.md) for current status and planned work.

Run the Direct2D graphical demo with:

```powershell
dotnet run --project Broiler.Graphics.Windows.Demo\Broiler.Graphics.Windows.Demo.csproj
```
