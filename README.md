# KeePassFaviconExtractor

A KeePass 2 plugin that attempts to find, download, convert, and cache
website favicons using multiple discovery methods.

## Feature highlights

- Entry and main-menu integration in KeePass (`Extract favicon` and `Diagnostics`).
- Multi-source favicon discovery with ranked candidate selection.
- Automatic retries for transient external-provider failures.
- SVG, ICO, PNG, and common web image handling with normalization to KeePass-compatible PNG.
- WEBP decode support via native `libwebp.dll` (x86/x64) when available locally.
- Defensive SVG conversion that rejects suspicious/cropped render output and falls back.
- Live extraction status window with cancel support, failure-only retry button, and auto-close on success.
- Diagnostics window with streaming probe output and per-provider summaries.

## Fallback strategy and reliability

The plugin does not depend on a single source. It progressively tries:

1. HTML `<link rel="icon">` candidates from the target page.
2. External favicon providers (Google S2, DuckDuckGo, Google faviconV2, and others).
3. Additional rescue/fallback probes when higher-ranked candidates fail.

If one provider is unavailable, the next sources are tried automatically.
This layered approach gives a high likelihood of finding an icon for most sites,
while still failing safely when conversion output is suspicious or unusable.

## Development setup

The project targets .NET Framework 4.8 and builds against the official
KeePass 2.61 portable distribution. Download the required development files
into the ignored `.deps` directory before opening or building the project:

```powershell
.\scripts\bootstrap-keepass.ps1
```

The script downloads the archive from SourceForge, verifies its pinned SHA-256
checksum, and extracts it to `.deps\KeePass\2.61`. It is safe to run repeatedly;
use `-Force` to replace an existing installation.

### Native WEBP decoder dependency

WEBP decoding uses native `libwebp.dll` files. Place them locally at:

- `native\x86\libwebp.dll`
- `native\x64\libwebp.dll`

The project is configured to copy these DLLs to the output directory.
These binaries are intentionally ignored by Git in this repository.

To build against an existing KeePass 2.61 installation instead, override the
MSBuild property:

```powershell
msbuild .\KeepassFaviconExtractor.csproj /p:KeePassDir="C:\Path\To\KeePass"
```

## Build a PLGX package (for KeePass-side compilation)

Create a testable PLGX package:

```powershell
.\scripts\build-plgx.ps1
```

If your runtime KeePass is elsewhere:

```powershell
.\scripts\build-plgx.ps1 -KeePassExePath "C:\Path\To\KeePass\KeePass.exe"
```

This creates:

`dist\FaviconExtractor.plgx`

To test it, copy the `.plgx` file into your KeePass `Plugins` directory and
restart KeePass.

## Build both DLL + PLGX release artifacts

Create both distribution formats in one command:

```powershell
.\scripts\build-release.ps1
```

Outputs:

- `dist\dll\FaviconExtractor\` (plugin DLL + dependency DLLs/PDB)
- `dist\plgx\FaviconExtractor.plgx`
- `dist\zip\FaviconExtractor-Release-dll.zip`
- `dist\zip\FaviconExtractor-plgx.zip`
- `dist\zip\FaviconExtractor-Release-hybrid.zip` (contains both DLL and PLGX packages)

If your runtime KeePass is elsewhere:

```powershell
.\scripts\build-release.ps1 -KeePassExePath "C:\Path\To\KeePass\KeePass.exe"
```

## Automatic packaging on Release build

Release builds package automatically by default (including Visual Studio UI
Release builds). The default is set in `Directory.Build.props`.

You can still control it explicitly from the command line:

```powershell
msbuild .\KeepassFaviconExtractor.csproj /p:Configuration=Release /p:PackagePluginOnBuild=true
```

Disable packaging for a specific Release build when needed:

```powershell
msbuild .\KeepassFaviconExtractor.csproj /p:Configuration=Release /p:PackagePluginOnBuild=false
```

## Optional: fail Build when tests fail

You can run tests as part of Build and fail the build on test failures using
`RunOnBuild`.

Command line:

```powershell
msbuild .\KeepassFaviconExtractor.slnx /p:RunOnBuild=true
```

Visual Studio UI builds can use the same flag by setting it in
`Directory.Build.props` (or in a local `.csproj.user` file):

```xml
<RunOnBuild>true</RunOnBuild>
```

Default is `true` for `Release` and `false` for non-Release configurations.
You can still override it explicitly with `/p:RunOnBuild=true|false`.

MSTest adapter resolution for build-time execution is centralized in
`Directory.Build.props`:

- `FaviconExtractorMSTestAdapterVersion` (currently `2.2.10`)
- `FaviconExtractorMSTestAdapterPath`

When upgrading MSTest adapter, update only
`FaviconExtractorMSTestAdapterVersion` (or override
`FaviconExtractorMSTestAdapterPath` directly).
