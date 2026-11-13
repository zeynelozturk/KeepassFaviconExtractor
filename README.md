# KeePass Favicon Extractor

A KeePass 2 plugin that finds and downloads website favicons. The extension
uses multiple discovery methods, therefore it has a higher likelihood of finding a favicon.

![Favicon Extractor screenshot](Assets/FaviconExtractor_screenshot.webp)
![Favicon Extractor screenshot](Assets/FaviconExtractor_screenshot_icon_found.webp)

## Highlights

- Entry right-click and main-menu integration in KeePass.
- Multi-source favicon discovery with ranked candidate selection: HTML parsing for fetching
  favicon (SVG, PNG, WEBP, ICO supported); fallback to multiple external services including
  Google, DuckDuckGo.
- Can download Android app icons (if AndroidApp1 or AndroidApp fields are present in the entry).
- 128x128 is the default size. Smaller icons aren't upscaled, but larger icons are downscaled to 128x128.
- Live extraction status and diagnostics windows.
- Multiple selection support for batch extraction.
- Optional icon chooser for a single website entry, with previews of distinct alternatives.
- "Download missing icons" feature for eligible entries in a database.
- Guard against storing duplicate icons in the KeePass database.
- Diagnostics window allows you to check if extension is working properly.

## Installation

- Download the latest version from [Releases](https://github.com/zeynelozturk/KeepassFaviconExtractor/releases)
- Extract the .zip file into your KeePass `Plugins` directory. This will create a FaviconExtractor subdirectory
  containing the plugin and its dependencies.
- Restart KeePass.

## Usage

- Right click an entry in KeePass and select "Extract website favicon" from the context menu.
- The plugin will attempt to find and download a favicon for the entry's URL,
convert it to a suitable format. If a favicon is found, it will be assigned to the entry.

![Favicon Extractor context menu](Assets/FaviconExtractor_context_menu.webp)

Note that this extension will change the icon of entry without confirmation.

We do not modify or delete previously stored icons.

To choose a different icon, select one entry and open **Tools → Favicon Extractor →
Choose another icon…**. The result window's **Look for other icons…** button is
enabled after a successful single-entry website extraction; clicking it stops the countdown. Select a preview,
then click **Use selected icon** to apply it. Cancel leaves the current icon unchanged.
The chooser supports HTTP/HTTPS website and direct-image URLs. Android-only entries
continue to use automatic extraction.

## Network and privacy behavior

- The plugin makes outbound HTTP/HTTPS requests to the target site's URL to discover icons.
- If direct discovery fails, it may query external providers (for example Google, DuckDuckGo,
  Favicone, Vemetric, Favicon.im, and Hunter logo endpoint).
- The optional chooser also checks these existing providers when website icons are found,
  to look for alternatives.
- External providers receive host/domain-based values (host and sometimes parent domain),
  not the full entry URL path/query.
- Redirects that end on private or loopback addresses can be blocked with
  `EnforcePrivateAddressBlocking` (default is compatibility-first).
- Native WEBP loading prefers plugin-local `native/x64` and `native/x86`; legacy broad path
  fallback is controlled by `AllowLegacyNativeLibrarySearchFallback`.
- Some response reads use explicit size caps (`MaxHtmlReadBytes`, `MaxPlaceholderHashReadBytes`)
  to reduce memory-risk from oversized responses.

## Licensing and Third-Party Notices

- This project is licensed under GPL-3.0. See [LICENSE](LICENSE).
- Third-party component notices and license texts are provided in [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).
- Release ZIP artifacts include both files.

## Contact

For questions, bug reports, or feature requests, please open an issue on GitHub,
or contact the author at `zeynel@defkey.com`.

If extension fails to download a favicon for a specific URL, please include the
text you see in the icon download window.

For build and development notes, see [DEVELOPMENT.md](DEVELOPMENT.md).
