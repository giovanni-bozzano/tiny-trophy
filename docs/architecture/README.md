# Architecture references

[Documentation](../README.md) | [README](../../README.md)

These references explain why Tiny Trophy is shaped the way it is. They are not an API reference:
types, constants and field lists live in `src/`, and the source is the final authority.

## Read in this order

| Reference | Answers |
|---|---|
| [Overview](Overview.md) | What the pieces are, how a scan flows and who owns lifetimes |
| [Achievement sources](Achievement-Sources.md) | How each source is scanned, parsed, merged and watched |
| [Threading and lifecycle](Threading-and-Lifecycle.md) | UI thread, background work, watchers, tray and shutdown |
| [Persistence and caching](Persistence-and-Caching.md) | Settings, Steam metadata, images and memory |
| [Packaging and updates](Packaging-and-Updates.md) | NativeAOT, CI artifacts and the update flow |

Operational material lives one level up: [build](../BUILD.md) and [testing](../TESTING.md).

## Invariants

These hold across every subsystem. A change that breaks one is a design change, not a local fix.

1. **One merged list is the truth.** `HomeViewModel` keeps the per-source scan results
   (`_sourceGames`) and rebuilds the merged, filtered list (`_allGames`) from them. Detail pages are
   refreshed from that merged list, never from a separate scan of their own.
2. **Sources are independent.** A scanner, parser, enricher or watcher failure is contained to its
   source. It must not stop another source, corrupt the merged list or crash the app.
3. **The UI thread owns bound state.** Scanning, parsing, file I/O, enrichment and watcher work run
   off the UI thread; results and events are marshalled back with `Dispatcher.UIThread`.
4. **Watchers are event driven and restartable.** No polling. `GameWatcherBase` owns debounce,
   known-state diffing, restart reporting and stale-run rejection; a stopped watcher's late
   detections must change nothing.
5. **Persistence is atomic and bounded to `AppPaths`.** Settings are written aside and moved into
   place; a corrupt file is preserved, not overwritten. Nothing writes outside the app data or cache
   roots at runtime.
6. **Bitmaps have one owner.** A control releases the bitmap it stops showing; nothing keeps a
   decoded bitmap alive after its control unloads. Image caches are on disk at drawn size, not in
   memory.
7. **The shipping build is NativeAOT.** No reflection-only APIs, no runtime code generation, and
   every serialized type is registered in `AppJsonContext`.
8. **Memory stays bounded without costing responsiveness.** Only visible rows are realized,
   caches are bounded, buffers are short lived, and memory is reclaimed only at natural idle
   boundaries. Never trade a visible delay, re-read or render stall for a lower number.

## What belongs here

Design rationale, ownership, ordering rules and failure containment. Package versions, file sizes
and current field lists belong in the project file, the settings JSON or the source, not prose. Name
the type or method for anything that can change. Explain the present shape; keep historical notes
only where they state a constraint a future change could otherwise violate.
