# Broiler.Graphics

A .NET 10 graphics component with a platform-neutral managed core and a Windows Direct2D
backend.

The core includes `BBitmap`, `BCanvas`, deterministic CPU raster operations, and
managed PNG/APNG, BMP, and JPEG codecs. The Windows assembly provides the Direct2D
backend, window/input integration, and demo application. See [ROADMAP.md](ROADMAP.md) for
current implementation status.

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
```

## License

Broiler.Graphics is licensed under the [Apache License 2.0](LICENSE). Third-party
material, if present, retains the license identified with that material. The license
provides the software on an “AS IS” basis, without warranties or conditions.
