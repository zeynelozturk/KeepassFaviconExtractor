# KeePass Favicon Extractor

A KeePass 2 plugin that finds, downloads, converts, and caches website favicons. The extension
uses multiple discovery methods, therefore it has a higher likelihood of finding a favicon.

## Highlights

- Entry (context menu) and main-menu integration in KeePass.
- Multi-source favicon discovery with ranked candidate selection: HTML fetch for fetching
favicon (SVG, PNG, WEBP, ICO supported), fallback to multiple external services.
- Live extraction status and diagnostics windows.
- Guard against storing duplicate icons in the KeePass database.
- Diagnostics window allows you to check if extension (mainly fallbacks) is working properly.

## Installation

- Download latest version of the plugin from [Releases](https://github.com/zeynelozturk/KeepassFaviconExtractor/releases)
- Extract the .zip file into your KeePass `Plugins` directory. 
The plugin is contained within a directory named `FaviconExtractor`, so it will be a subdirectory of your `Plugins` directory.
- Restart KeePass.

## Usage

- Right click an entry in KeePass and select "Extract Favicon" from the context menu.
- The plugin will attempt to find and download a favicon for the entry's URL,
convert it to a suitable format. If a favicon is found, it will be assigned to the entry.

Note that this extension will change the icon of entry without confirmation.

We do not modify or delete previously stored icons.

## Contact

For questions, bug reports, or feature requests, please open an issue on GitHub,
or contact the author at `zeynel@defkey.com`.

If extension fails to download a favicon for a specific URL, please include the
text you see in the icon download window.

For build and development notes, see [DEVELOPMENT.md](DEVELOPMENT.md).
