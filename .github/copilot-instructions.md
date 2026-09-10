# Copilot Instructions

## Project Guidelines
- User prefers external plugin identity to be 'FaviconExtractor' (DLL/PLGX/KeePass-visible name), while keeping internal project/class/file names using 'KeePassFaviconExtractor' when convenient.
- User prefers minimal behavioral changes to existing flow and wants extract-related errors/results shown in the live status window instead of popup dialogs.
- User prefers softer UI styling in plugin dialogs, specifically rounded icon preview corners and gray (not black) borders.
- For diagnostics, a provider should be considered successful if any of its probes succeed; transient probe timeouts should not drive an overall failure tone.