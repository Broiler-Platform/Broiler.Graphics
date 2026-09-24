# CI, packages, and releases

The Broiler component repositories use a unified workflow structure and release tooling.
`Directory.Packages.props` centrally manages external and cross-component dependency versions.
Release versions are separate: `eng/Broiler.Packaging.props` supplies defaults, with repository
overrides in `Directory.Build.props`. All packages within this repository share a single version;
each repository advances its own preview sequence.

## Build, test, and pack

Requirements:
- .NET 10 SDK
- Node.js 24 for release scripts
- PowerShell 7 (or Windows PowerShell 5.1+) for packaging and verification
- Bash (Git Bash on Windows) for the test runner script

To build, test, and package locally:

```sh
dotnet build Broiler.Graphics.slnx -c Release
bash ./eng/run-tests.sh Release
node --test eng/resolve-preview-version.test.mjs
pwsh -File eng/pack.ps1
```

Use `Debug` or `Release`; platform-suffixed solution configurations are obsolete.
The test script runs the test suites appropriate to the host platform. Platform-specific suites
(such as Windows Direct2D or Linux) build explicitly because they are excluded from the standard
solution build.

`eng/pack.ps1` enumerates **every packable project** in the solution (including platform providers
excluded from default builds) and builds packages into the target directory (`artifacts` by default).
It validates:
- All 7 packages are generated with matching versions and IDs.
- Package metadata (README.md, icon.png, license expression).
- Inter-package dependency versions within the component.
- Assembly binaries and XML documentation files for IntelliSense.
- Snupkg symbol packages.

Use Windows to pack the complete package set (as the Direct2D backend targets `net10.0-windows`).
The output directory must be empty before packing to prevent publishing stale artifacts; specify
`-Output <directory>` for alternate locations. An optional `-Version 0.1.0-preview.N` parameter stamps
assembly and package versions together.

## Package feeds

All Broiler packages and third-party dependencies are restored directly from **[NuGet.org](https://www.nuget.org)**.
No private registries or authentication tokens are required.

`NuGet.config` explicitly clears inherited machine-wide sources, disabled sources, and source mappings,
mapping all packages (`*`) strictly to `nuget.org`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
  <disabledPackageSources>
    <clear />
  </disabledPackageSources>
  <packageSourceMapping>
    <clear />
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
```

This ensures hermetic, deterministic restores across developer machines and CI agents.
Upstream dependencies (`Broiler.Native.*`, `Broiler.Media.*`, `Broiler.Input.*`) are pinned in
`Directory.Packages.props` and resolve directly from NuGet.org.

## CI and Publish pipelines

### Continuous Integration (`ci.yml`)

The CI workflow runs on pushes to `main` and on pull requests:
1. Matrix builds in `Release` configuration on `ubuntu-latest` and `windows-latest`.
2. Runs Node.js unit tests for preview version calculation (`eng/resolve-preview-version.test.mjs`).
3. Runs the test suite via `eng/run-tests.sh Release`.
4. On `windows-latest`, runs `eng/pack.ps1` to produce and validate all 7 packages.
5. Uploads the packages as the `nuget-packages` workflow artifact.

### Publishing (`publish.yml`)

Releases are published exclusively to **NuGet.org**.

The workflow is triggered either:
- Manually via `workflow_dispatch` with optional inputs:
  - `dry-run`: Validate packages and simulate publication without pushing (default: `true`).
  - `version-suffix`: Explicit preview version suffix (e.g., `preview.7`). If empty, the next unused preview number is chosen automatically.
- Automatically by pushing a version tag (e.g., `git tag v0.1.0-preview.7 && git push origin v0.1.0-preview.7`), which performs a non-dry-run publish to NuGet.org.

The publication workflow executes in four stages:

1. **Resolve version**:
   `eng/resolve-preview-version.mjs` inspects NuGet.org's PackageBaseAddress flat container for all 7 shipping packages, identifies already published preview versions, and determines the next available preview version.
   If pushing via a tag, the tag version is verified against the release line and must be unused.

2. **Validate and pack**:
   Calls `ci.yml` with the resolved version to build, test, and pack all packages.

3. **Verify consumer restore**:
   Runs `eng/verify-feed.ps1 -Target nuget` on the packaged artifacts.
   This creates an isolated test consumer project and NuGet cache to verify that every package and all its transitive dependencies can be restored successfully from the staged release and NuGet.org before uploading.

4. **Push to NuGet.org**:
   When `dry-run` is `false`, pushes all `.nupkg` and `.snupkg` symbol packages to NuGet.org:
   ```sh
   dotnet nuget push 'artifacts/*.nupkg' --source https://api.nuget.org/v3/index.json --api-key "$NUGET_API_KEY"
   ```
   Publishing requires the repository secret `NUGET_TOKEN` (or `NUGET_API_KEY`).

### Local verification

Developers can run the same package and feed verification locally:

```powershell
pwsh -File eng/pack.ps1 -Output artifacts
pwsh -File eng/verify-feed.ps1 -Target nuget -Packages artifacts
```
