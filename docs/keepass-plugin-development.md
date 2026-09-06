# KeePass 2.x Plugin Development Reference

This document summarizes the relevant KeePass 2.x plugin-development requirements for KeePassFaviconExtractor.

## Authoritative source

The official KeePass 2.x plugin-development documentation is:

https://keepass.info/help/v2_dev/plg_index.html

When this document conflicts with the actual KeePass 2.61 source, assemblies, or official documentation, the actual KeePass 2.61 API takes precedence.

Do not invent KeePass APIs based on assumptions or examples from other versions.

## Project requirements

KeePass 2.x plugins should be developed as:

- C#
- Class Library targeting the .NET Framework
- Not .NET Standard
- Not .NET Core / modern .NET

The official KeePass documentation currently recommends using the latest portable KeePass ZIP package during plugin development.

## KeePass reference

The plugin project must reference the official `KeePass.exe` from the KeePass distribution.

Do not reference:

- a development snapshot
- a locally compiled KeePass build
- an unofficial KeePass build

The KeePass reference provides the `KeePass` and `KeePassLib` namespaces.

## Plugin class

Every KeePass 2.x plugin must derive from:

`KeePass.Plugins.Plugin`

The main plugin class follows a naming convention.

If the DLL is:

`KeePassFaviconExtractor.dll`

the namespace should be:

`KeePassFaviconExtractor`

and the main plugin class should be:

`KeePassFaviconExtractorExt`

The class derives from `Plugin`.

Example structure:

```csharp
using KeePass.Plugins;

namespace KeePassFaviconExtractor
{
    public sealed class KeePassFaviconExtractorExt : Plugin
    {
        private IPluginHost m_host;

        public override bool Initialize(IPluginHost host)
        {
            if(host == null) return false;

            m_host = host;
            return true;
        }

        public override void Terminate()
        {
        }
    }
}
```

## Initialization

`Initialize(IPluginHost host)` is the primary plugin initialization method.

KeePass calls it immediately after loading the plugin.

Initialization work should be performed here rather than in the plugin constructor.

Return:

- `true` when initialization succeeds
- `false` when initialization fails

Returning `false` causes KeePass to unload the plugin.

The `IPluginHost` reference provides access to KeePass functionality such as the main menu and currently opened database.

## Termination

Override `Terminate()` when cleanup is required.

KeePass calls it shortly before unloading the plugin.

Release resources here rather than relying on a destructor/finalizer.

For KeePassFaviconExtractor this is particularly relevant for:

- background operations
- HTTP resources
- timers
- event subscriptions
- cached UI objects
- other disposable resources

The plugin cannot prevent KeePass from unloading after `Terminate()` returns.

## Menu integration

A plugin can provide KeePass menu items by overriding:

`GetMenuItem(PluginMenuType t)`

The method returns a new `ToolStripMenuItem` for the requested menu location, or `null` when the plugin has no item there.

Do not cache the returned menu item.

KeePass owns the returned menu item and may request it multiple times or place it in multiple locations.

For KeePassFaviconExtractor, menu functionality can later be used for commands such as:

- Fetch favicon
- Refresh favicon
- Fetch favicons for selected entries
- Options

The menu system should not be implemented until the core favicon functionality is working.

## Assembly metadata

KeePass identifies plugins using the assembly version-information block.

Important fields:

- **Title:** full plugin name
- **Description:** short plugin description
- **Company:** author name
- **Product:** must be exactly `KeePass Plugin`
- **Assembly Version:** plugin version
- **File Version:** plugin version

The file and assembly versions should use a comparable versioning scheme.

Do not use automatic `*` version generation.

The namespace must match the DLL filename without the extension.

## Plugin naming

If `KeePass` is part of the plugin name, it should be directly attached to another word.

Valid:

`KeePassFaviconExtractor`

Avoid:

`KeePass Favicon Extractor`

For this project the intended name is:

`KeePassFaviconExtractor`

Therefore:

```text
DLL:       KeePassFaviconExtractor.dll
Namespace: KeePassFaviconExtractor
Class:     KeePassFaviconExtractorExt
```

## DLL and PLGX

KeePass supports two plugin formats:

- DLL
- PLGX

A DLL plugin is normally preferred and should be provided.

PLGX can additionally be provided when compatibility with custom KeePass builds is desired.

DLL advantages include:

- no compilation on the user's machine
- Authenticode signing support
- slightly faster loading
- no PLGX compilation cache

PLGX advantages include better compatibility with custom KeePass builds because KeePass can adjust its own reference before compiling the plugin.

For the initial development of KeePassFaviconExtractor, use a normal DLL project.

PLGX packaging can be considered later.

## PLGX considerations

If PLGX support is added later:

- PLGX supports C# only.
- The compiler included with the .NET Framework supports at most C# 5.
- The project should therefore use C# 5-compatible syntax when PLGX compatibility is required.
- PLGX can read project information from the `.csproj`.
- Third-party DLL references can be included if they are located within the plugin source directory or a subdirectory.
- Project-to-project dependencies are not supported by PLGX.
- Linked resources are not supported.

These restrictions should not unnecessarily constrain the initial DLL development unless PLGX distribution is planned.

## Update checking

KeePass supports plugin update checking.

A plugin can override `UpdateUrl` to provide the HTTPS URL of a version-information file.

Update checking is optional.

Do not implement it until the plugin has a stable release process.

If implemented, use HTTPS.

## Security and compatibility principles

For this project:

1. Never assume a KeePass API exists.
2. Verify KeePass APIs against the actual KeePass 2.61 reference/source.
3. Do not initialize plugin functionality in the constructor.
4. Keep long-running operations off the KeePass UI thread.
5. Clean up resources in `Terminate()`.
6. Do not cache KeePass menu items.
7. Avoid depending on undocumented KeePass internals when the public plugin API is sufficient.
8. Keep the core favicon functionality independent from KeePass UI code where possible.

## Initial implementation strategy

Start with the smallest valid KeePass plugin:

1. Create the .NET Framework class library.
2. Reference the official KeePass 2.61 `KeePass.exe`.
3. Create `KeePassFaviconExtractorExt`.
4. Implement `Initialize`.
5. Implement `Terminate`.
6. Set the required assembly metadata.
7. Build the DLL.
8. Load it into KeePass 2.61.
9. Verify that KeePass recognizes and loads the plugin.
10. Only then begin implementing favicon discovery.

This establishes that the KeePass integration works before adding the considerably more complicated favicon functionality.