# KeePassFaviconExtractor

> ⚠️ Work in progress — not ready for use

A KeePass 2 plugin that attempts to find, download, convert, and cache
website favicons using multiple discovery methods.

## Status

This project is currently under active development.

The plugin may not compile or function correctly yet. APIs and
implementation details may change substantially.

Do not use this version with important KeePass databases.

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

`dist\KeePassFaviconExtractor.plgx`

To test it, copy the `.plgx` file into your KeePass `Plugins` directory and
restart KeePass.

## Build both DLL + PLGX release artifacts

Create both distribution formats in one command:

```powershell
.\scripts\build-release.ps1
```

Outputs:

- `dist\dll\KeePassFaviconExtractor\` (plugin DLL + dependency DLLs/PDB)
- `dist\plgx\KeePassFaviconExtractor.plgx`
- `dist\zip\KeePassFaviconExtractor-Release-dll.zip`
- `dist\zip\KeePassFaviconExtractor-plgx.zip`
- `dist\zip\KeePassFaviconExtractor-Release-hybrid.zip` (contains both DLL and PLGX packages)

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
