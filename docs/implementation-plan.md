# KeePass Favicon Plugin — Implementation Plan

## Goal

Create a KeePass 2.x plugin in C# targeting KeePass 2.61 that aggressively finds, downloads, converts, validates, and caches website icons for KeePass entries.

The plugin should attempt multiple discovery strategies before giving up. It should handle modern SVG favicons and convert them into a format that KeePass can use reliably.

The goal is not merely to support standards-compliant favicons. The plugin should make a serious effort to find an icon even when the website is poorly configured.

## 1. KeePass integration

- Target KeePass 2.61 compatibility.
- Implement the standard KeePass 2 plugin architecture.
- Add a configurable favicon-fetching feature.
- Detect URLs from KeePass entries.
- Avoid modifying the entry's URL or other user data.
- Store normalized icons in the active database's KeePass custom-icon collection.
- Assign custom icons to entries using their KeePass custom-icon UUIDs.
- Reuse a single custom icon across entries when their normalized PNG data is identical.
- Avoid repeatedly downloading the same favicon.

## 2. Favicon discovery pipeline

Try methods in priority order.

### Level 1 — Inspect the actual webpage

Fetch the URL with `HttpClient`.

Parse HTML and inspect `<link>` elements.

Recognize at least:

- `rel="icon"`
- `rel="shortcut icon"`
- `rel="apple-touch-icon"`
- `rel="apple-touch-icon-precomposed"`
- other reasonable icon-related `rel` values

Handle:

- relative URLs
- absolute URLs
- protocol-relative URLs
- `<base href>`
- redirects
- different MIME types
- multiple icon declarations
- `sizes` attributes
- `type` attributes

If multiple candidates exist, rank them rather than simply taking the first one.

Prefer:

1. `rel="icon"` with an appropriate image type
2. larger usable icons
3. PNG
4. SVG
5. ICO
6. Apple touch icons
7. other usable raster formats

Do not reject an icon merely because its declared MIME type is missing or incorrect. Inspect the downloaded content as well.

## 3. Standard fallback

If HTML inspection produces nothing usable:

Try:

`https://host/favicon.ico`

Also consider the original scheme where appropriate.

Follow redirects.

Validate the downloaded file rather than assuming that HTTP 200 means an icon exists.

## 4. Additional website probing

If the normal methods fail, make additional reasonable attempts.

Potential candidates include:

- root-page HTML instead of only the original page
- common favicon filenames
- common icon filenames
- icons referenced through metadata

Do not generate an unlimited number of requests.

Use a configurable maximum number of HTTP requests per favicon lookup.

Use short connection/read timeouts.

## 5. Search-engine fallback

If all direct website methods fail, optionally use a search-engine fallback.

The purpose is to find an image associated with the website, not merely to find another webpage.

Open the search in the user's normal web browser when configured to do so.

Construct a query using the domain/site name, for example:

`example.com favicon`

or:

`site:example.com favicon`

The plugin should attempt to identify image results and obtain a candidate image URL if technically possible.

Important:

- Do not assume Google's HTML structure is stable.
- Do not make the implementation dependent on undocumented Google internals if this would be fragile.
- Isolate search-engine-specific code behind an interface so another provider can be added later.
- Treat search engines as a last-resort discovery mechanism.
- Respect the search engine's terms and avoid aggressive automated scraping.

If automatic extraction from the search page is unreliable, provide a browser-based/manual fallback where the user can select or copy an image URL.

## 6. Image acquisition

For every candidate:

- Download with `HttpClient`.
- Follow redirects with limits.
- Enforce a maximum response size.
- Reject obviously non-image responses.
- Inspect the actual file signature/content rather than trusting `Content-Type`.
- Handle servers that return `text/html` while claiming an image MIME type.
- Reject corrupted images.
- Prevent decompression-bomb-style oversized images.
- Avoid indefinitely waiting for slow servers.

Supported source formats should include at least:

- ICO
- PNG
- JPEG
- WebP
- GIF where useful
- SVG

## 7. SVG handling

SVG must be treated as a first-class favicon format.

If the discovered favicon is SVG:

- Parse/validate the SVG safely.
- Determine its intrinsic dimensions/viewBox when possible.
- Render it to a raster image.
- Produce an icon suitable for KeePass.

Do not simply rename an `.svg` file to `.png`.

The SVG renderer must not execute arbitrary scripts or external resources.

Disable or reject:

- JavaScript
- external document references
- unnecessary external network resources
- dangerous embedded content

The conversion pipeline should produce a clean raster image.

## 8. Icon normalization

Normalize every successful icon into a consistent representation.

Recommended internal processing:

1. Decode source image.
2. Determine usable dimensions.
3. Preserve transparency.
4. Render onto an appropriate transparent canvas when necessary.
5. Resize using high-quality interpolation.
6. Generate the final KeePass-compatible icon representation.

Handle ICO files containing multiple resolutions.

Prefer the largest reasonable source image without unnecessarily processing enormous images.

Do not upscale tiny icons unnecessarily unless required by KeePass.

## 9. Candidate scoring

Create a scoring system rather than a simple first-success approach.

Example factors:

- declared `rel="icon"`: high score
- valid favicon MIME type: positive
- larger dimensions: positive
- SVG with valid viewBox: positive
- PNG: positive
- ICO: positive
- Apple touch icon: moderate
- search-engine result: lower priority
- suspicious/non-image content: reject

The first candidate that passes validation is not necessarily the best candidate.

Collect candidates and select the highest-quality usable icon.

## 10. KeePass icon storage and caching

Use KeePass's database-level custom icon store as the authoritative persistent store for icon image data.

- Store normalized PNG bytes as KeePass custom icons in the active database.
- Assign the resulting custom-icon UUID to each applicable entry.
- Allow multiple entries to reference the same custom-icon UUID.
- Hash normalized PNG data and reuse an existing matching custom icon instead of adding duplicate image data.
- Mark the database as modified and request the appropriate KeePass icon/UI refresh after making changes.
- Do not delete or replace custom icons without accounting for references from entries, groups, and entry history.

Do not create a separate persistent filesystem cache containing duplicate favicon image data.

Use an in-memory lookup during a plugin session to avoid repeated work. The lookup key should be based on the normalized website origin/domain rather than the complete page URL where appropriate.

For example:

`https://www.example.com/a`

and

`https://www.example.com/b`

should normally be able to share the same favicon and custom-icon UUID.

For the initial version, treat an existing assigned custom icon as cached and provide explicit force-refresh behavior.

If automatic expiration or revalidation is added, store only the required metadata in database-scoped KeePass custom data, using plugin-specific keys. Optional metadata may include:

- normalized host/origin
- custom-icon UUID or normalized image hash
- source URL
- acquisition method
- timestamp
- optional failure timestamp

Do not store credentials or complete KeePass entry data in cache metadata.

Avoid fetching the same site's favicon every time KeePass starts.

Provide a way to force-refresh an icon.

## 11. Failure handling

The plugin should distinguish between:

- no icon found
- network failure
- invalid icon
- unsupported format
- blocked request
- timeout
- search fallback failure

Failures should not cause KeePass to fail or become unresponsive.

Favicon acquisition should preferably happen asynchronously.

Never block the KeePass UI thread on network requests.

## 12. Security

Treat every downloaded file and webpage as untrusted.

Important protections:

- HTTPS preferred.
- Strict request timeouts.
- Maximum download size.
- Maximum redirect count.
- Validate image contents.
- Safely parse HTML.
- Safely parse SVG.
- Do not execute downloaded content.
- Do not load arbitrary external resources from SVG.
- Avoid SSRF-style behavior where practical.
- Do not send KeePass passwords, usernames, or other entry secrets to search engines.
- Search fallback queries should contain only the website/domain information necessary to identify the site.

The plugin must never transmit KeePass credentials as part of favicon discovery.

## 13. Browser fallback

Provide a final user-assisted fallback.

If automatic acquisition fails:

- Offer "Search for favicon" in the plugin UI.
- Open the search in the user's default browser.
- Make it easy for the user to provide an image URL.
- Download and validate the selected image through the same secure image-processing pipeline.

This ensures the user can still obtain an icon even when automatic discovery fails.

## 14. Configuration

Provide settings for:

- Enable/disable automatic favicon fetching
- Enable/disable search-engine fallback
- Open browser for search fallback
- Cache duration
- HTTP timeout
- Maximum favicon download size
- Maximum redirects
- Maximum requests per lookup
- Force HTTPS where appropriate
- Refresh favicon manually

Reasonable safe defaults should be provided.

## 15. Architecture

Keep the implementation modular.

Suggested components:

`FaviconPlugin`
- KeePass integration

`FaviconService`
- orchestrates the discovery pipeline

`FaviconCandidate`
- URL, source, MIME type, dimensions, score

`HtmlFaviconDiscoverer`
- parses webpage HTML

`WellKnownFaviconDiscoverer`
- `/favicon.ico` and common locations

`MetadataIconDiscoverer`
- additional metadata/icon references

`SearchFaviconDiscoverer`
- search-engine fallback

`FaviconDownloader`
- HTTP acquisition and validation

`ImageDecoder`
- detects and decodes image formats

`SvgRenderer`
- safe SVG rasterization

`IconNormalizer`
- produces final normalized icon

`KeePassIconStore`
- stores normalized PNG icons in the active database's custom-icon collection
- deduplicates icons and assigns custom-icon UUIDs to entries

`FaviconMetadataStore`
- optionally stores database-scoped source and revalidation metadata

`FaviconScorer`
- candidate ranking

Each discovery mechanism should implement a common interface so new strategies can be added later.

## 16. Testing

Create tests for at least:

- normal `<link rel="icon">`
- relative favicon URLs
- absolute favicon URLs
- redirected favicon URLs
- `/favicon.ico`
- PNG favicon
- ICO favicon
- SVG favicon
- malformed SVG
- invalid MIME type
- missing MIME type
- HTML returned from an image URL
- multiple favicon declarations
- high-resolution icons
- tiny icons
- transparent icons
- inaccessible websites
- timeout
- redirect loops
- oversized downloads
- malformed images
- domains containing unusual characters
- URLs containing paths/query strings
- websites with no favicon
- storing a normalized PNG as a KeePass custom icon
- assigning a custom-icon UUID to an entry
- reusing an existing icon with identical normalized PNG data
- sharing one custom icon across entries from the same site
- preserving icons referenced by entries, groups, or entry history
- force-refreshing an existing assigned icon

Include integration tests where practical, but keep unit tests independent of external websites.

## 17. Important implementation principle

Do not assume websites follow the favicon specification correctly.

The discovery system should be:

`standard → fallback → probing → search → user-assisted`

but every stage must remain bounded, safe, asynchronous, and cacheable.

The objective is **maximum practical favicon coverage**, not theoretical 100% coverage.