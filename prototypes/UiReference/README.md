# nomozip reference reproduction · x64 preview

Independent WPF prototype of concept 02 (archive browser and image preview).
No production services or real password library. Generated in-memory ZIP sample;
search, sorting, selection, image/text preview and paced in-memory extraction work.
The reference direction was adopted on 2026-10-17 (core source 967175a).
Files home, create, vault, checksum, settings, updates and dialog design surfaces
are now available. Use More > Files home / Create / extraction confirmations;
the sidebar opens vault, tools and settings. No installation.
Requires shared .NET 10 Windows Desktop runtime, x64 Windows.

The original nomozip desktop icon is retained. Function icons: Microsoft Fluent
System Icons Regular 20, MIT (Icons/LICENSE). Photograph credit: Assets/ATTRIBUTION.md.
Screenshots are restricted to isolated GitHub CI; the normal application does not capture.

Extraction runs in a dedicated sample task workspace. Progress ring, bar and percentage
share the displayed value; pause freezes all progress. Header hover/press uses a compact
rectangle and keyboard focus is inset. Native input helpers are used only by isolated CI.

Expanded forms edit preview memory only. Checksum tools hash the actual generated
ZIP payload. Update previews do not contact a server or claim a latest version.
No production extraction, creation, password database or configuration is changed.
`--capture-pages` is an isolated-CI-only entry; `--capture` retains core checks.
