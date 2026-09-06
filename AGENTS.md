# Project Instructions

Before making implementation changes:

1. Read `docs/implementation-plan.md`.
2. Read `docs/keepass-plugin-development.md`.
3. Inspect the actual KeePass 2.61 source or referenced assemblies available
   to the project before relying on any KeePass API.
4. Treat the actual KeePass 2.61 API as authoritative if it conflicts
   with the documentation in `docs/`.
5. Do not invent KeePass APIs or assume APIs from other KeePass versions.

## KeePass API Reference

When working with KeePass plugin APIs, do not rely solely on general
knowledge or assumptions.

First inspect the actual KeePass 2.61 source code and referenced
assemblies available to the project.

If you are genuinely unsure about KeePass plugin behavior or an API,
consult the official KeePass 2.x plugin development documentation:

https://keepass.info/help/v2_dev/plg_index.html

If the documentation and the actual KeePass 2.61 source disagree,
the actual KeePass 2.61 source/API takes precedence.

When consulting external documentation, verify that it applies to
KeePass 2.x and not KeePass 1.x or another KeePass-related project.