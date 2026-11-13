# Choose another icon — approved plan

Branch: `feature/choose-another-icon`, based on local `master`.

## Goal and placement

Keep Extract website favicon a single action that downloads and assigns automatically. Offer a separate, optional way to choose a different result.

| Location | Behavior |
| --- | --- |
| Existing entry context-menu command | Unchanged: clicking Extract website favicon immediately starts automatic extraction, including multiple-selection support |
| Tools → Favicon Extractor | Add Choose another icon… for exactly one selected entry; independently accessible after the result window closes |
| Single-entry extraction result window | Add a secondary Choose another icon… button after successful completion; clicking it opens the same chooser for the entry that was processed |
| Batch result windows | No chooser button in the first version; select an individual entry and use Tools |

Do not convert the existing entry context-menu command into a submenu, require a modifier key, or make the preview image an undisclosed clickable control. GetMenuItem returns one ToolStripMenuItem; avoid direct menu manipulation to add a sibling.

Preserve the successful result window's three-second auto-close. Using the new button stops its countdown before opening the chooser. Check keyboard focus and countdown interaction. Tools remains available later.

## Chooser interaction

1. Capture the database and entry on invocation; later selection changes cannot change the target.
2. Show the entry title and current icon. Discover asynchronously with progress and cancellation.
3. Display successfully downloaded, normalized candidates as available, deduplicated by normalized image content.
4. Show normal list-size and enlarged previews, source, and decoded source dimensions where available. Distinguish source dimensions from normalized output. Keep URLs in details/tooltips.
5. Selecting a tile only selects it. Explicit Use selected icon assigns and closes. Cancel, close, failures, and empty results leave the current icon untouched.
6. Do not preselect a new candidate or let Enter accidentally replace the icon during loading. Mark the current normalized image as already in use.

Existing custom icons are not required; built-in icons are supported.

## First-version scope

- One website entry at a time, with existing HTTP/HTTPS and direct-image handling.
- Real alternatives from existing discovery providers; no new integrations.
- Bounded search continuing beyond automatic extraction's conditional fallback decisions.
- Partial results remain usable when sources fail or time out. Explain when no distinct alternative exists.
- No persistent candidate cache, provenance, scheduled refresh, automatic quality audit, editor, or database-wide replacement.
- Android lookup remains on its automatic path. Disable the chooser for Android-only entries with an explanation.
- Alternate URL input is a possible follow-up.

## Shared boundary

Do not add a chooser flag to the automatic workflow or duplicate its candidate-processing loop in the form.

1. Candidate preparation downloads and normalizes without KeePass mutation. Return identity/source, decoded dimensions when available, and normalized PNG bytes. Preserve timeout, safety, and cancellation behavior. Keep PwEntry and WinForms out.
2. An alternative-search coordinator discovers and prepares bounded results, reports progress, and deduplicates normalized images without assignment. Preserve automatic discovery defaults.
3. A dedicated form owns presentation, selection, and image disposal. It returns a selected prepared result; it performs no network calls or KeePass mutation.
4. Plugin integration captures context, starts/closes the chooser, revalidates the target, and applies through the existing assigner. Both entry points invoke the same command.

Automatic extraction reuses preparation while preserving candidate order, fallback/rescue behavior, timing, and assignment semantics. Keep unrelated nested forms and bulk workflows in place. Do not store global candidate lists or workflow state on menu items. Start a fresh search each invocation.

## Lifecycle and assignment

- Share operation exclusion with extraction; search/apply cannot race another plugin operation.
- Capture database, entry identity, and original icon state. Before apply, verify the database is open and the exact entry still belongs to it. Abort if its icon changed concurrently; require opening a fresh chooser.
- Lock/close, termination, and chooser close cancel outstanding work. Ignore late callbacks to disposed controls.
- Reuse normalized-icon deduplication on assignment. Browsing creates no database icons and deletes no old icons.
- Inspect actual KeePass 2.61 source/assemblies for events, membership checks, history, and mutation behavior. Preserve the existing off-UI-thread assignment workaround.
- The form owns and disposes preview Bitmaps. Search results hold managed bytes and metadata.

## Implementation order

1. Establish the feature branch and save the plan under docs.
2. Extract preparation and verify automatic/rescue/batch behavior is preserved.
3. Add bounded alternative discovery, deduplication, and cancellation.
4. Add the chooser without database mutation before Apply.
5. Wire Tools and the successful single-entry shortcut to one workflow.
6. Run targeted tests and check the plugin in KeePass.

## Acceptance checks

- Original right-click extraction assigns automatically and keeps its close schedule; batch and Retry remain functional.
- Tools works without a prior extraction and after the result closes.
- The result shortcut uses the processed entry and stops the timer.
- Cancel, failures, no alternatives, and no selection do not modify database icons or the entry.
- Apply changes only the captured entry and reuses existing normalized icons.
- Duplicates, tiny icons, SVG/WebP, failed/slow providers, large lists, lock/close, selection changes, entry removal, and termination behave predictably.
- Keyboard navigation, previews, and labels work at 125% and higher DPI. Show decoded dimensions separately from output dimensions.
