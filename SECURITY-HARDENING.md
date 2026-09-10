# Security Hardening Checklist (Lightweight)

This document tracks pragmatic hardening items for KeepassFaviconExtractor.

## Goals
- Reduce risk from unsafe native library loading.
- Reduce risk from local/private network redirect targets during icon discovery.
- Avoid unbounded HTTP response reads in selected flows.
- Keep behavior changes minimal and predictable.

## Non-goals
- Deep security redesign.
- Strict sandboxing.
- Heavy cryptographic attestation requirements.

## Planned Changes

### 1) Native DLL load policy
- Prefer plugin-local native paths first (`native/x64`, `native/x86`).
- Keep legacy fallback search behavior behind an opt-in compatibility switch.
- Add diagnostic visibility for selected load path behavior.

### 2) Network redirect/address safety
- Add helper to detect loopback/private/link-local hosts.
- Block discovered/downloaded final URLs that resolve to private/loopback when enabled.
- Keep this check configurable to avoid breaking uncommon private-network setups.

### 3) Response-size hardening
- Add HTML response size cap and stream-limited reading.
- Add bounded placeholder hashing reads for external favicon providers.
- Keep image download cap enforcement as-is and reuse shared limits where possible.

### 4) Tests and docs
- Add unit tests for address classification and bounded reads.
- Add README notes about external providers, outbound network usage, and privacy.

## Suggested defaults
- `EnforcePrivateAddressBlocking = false` (compatibility first; can be enabled later).
- `AllowLegacyNativeLibrarySearchFallback = true` (compatibility first; can be disabled later).
- `MaxHtmlReadBytes = 262144` (256 KB).
- `MaxPlaceholderHashReadBytes = 131072` (128 KB).
