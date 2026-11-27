# Threading and lifecycle

[Architecture index](README.md) | [Documentation](../README.md)

## The UI thread

Avalonia creates one UI thread. Everything a control binds to — view models, observable
collections, `InfoMessage`, `Games` — is owned by it. Work that touches disk or the network runs
elsewhere and comes back through `Dispatcher.UIThread`:

| Producer | Marshalling |
|---|---|
| Watcher `AchievementsChanged` | `Dispatcher.UIThread.Post` → `HomeViewModel.RefreshGameAsync` |
| Watcher `AchievementUnlocked` | `Dispatcher.UIThread.Post` → notification window |
| Steam key validation | `Dispatcher.UIThread.Post` → `MainViewModel.ApiKeyWarning` |
| Scan and enrichment progress | `Progress<T>` captured on the UI thread |
| Update dialog | `Dispatcher.UIThread.InvokeAsync` |

Scans, single-game refreshes and watcher startup use `Task.Run`. A scan reads every achievement
file synchronously, so running it on the UI thread would freeze the window; starting a watcher also
re-reads every file to seed its known state.

## Watchers

`GameWatcherBase` owns the shared machinery:

- **Seeding.** `InitializeKnownState` records every known game and its unlocked ids, so the first
  detection does not re-notify existing unlocks.
- **Debounce.** A file event schedules a detection 800 ms later; a newer event for the same key
  cancels the pending one.
- **Buffers.** Each `FileSystemWatcher` gets a 64 KB buffer, because games write several files at
  once and the default 8 KB buffer overflows and drops events.
- **Overflow recovery.** A watcher `Error` re-schedules every known game, so a missed unlock is
  recovered on the next read.
- **Generations.** `Stop` bumps a generation; a detection that started before it is stale and may
  not change state or raise events.
- **Restart reporting.** `Start` compares the newly seeded set with the previous one and reports
  games that appeared or disappeared, unless `reportChanges` is false (settings save).
- **Per-game processing.** Detections for one key are serialized with a reference-counted lock that
  is removed when unused.

The concrete watchers define their own file filters. They are event driven on purpose: no polling,
no timers, no rescan loop.

## Startup

1. `Program.Main` claims the single-instance mutex, then extracts embedded native libraries.
2. Avalonia starts; `App.Initialize` installs the image loader and deletes leftovers from a previous
   update.
3. `AppServices` loads settings synchronously, builds scanners, enrichers, the achievement service,
   the view models and the watchers.
4. `MainWindow` is created; the post-update flag is consumed and its banner shown if present; the
   tray icon is created.
5. Initial setup replaces the home view when no API key is configured; otherwise services start.
6. Services subscribe to watcher/API events, start watchers on a background thread and trigger the
   first game load.
7. A background update check runs if enabled, showing the update dialog on the UI thread.

Closing the window hides it (unless the user chose Exit). Hiding clears the bound view through
`SuspendView` so images can be unloaded; showing it again restores the same view with `ResumeView`.

## Memory boundaries

The app holds the merged game list and the metadata it needs; it does not hold decoded images.
After the last notification closes while the window is hidden, `MemoryTrimmer` runs a GC and, on
Windows, trims the process working set. A start-minimized launch trims once the first load finishes.
These are the only scheduled trims: there is no timer, no polling and no trim on interaction, so
visible work never pays for a lower idle number.

## Shutdown

`App.ExitRequested` and the tray's Exit both funnel into `App.ExitApp`, which sets `AllowClose`,
disposes `AppServices` (watchers, Steam API), the tray icon and the image loaders, then shuts down.
`AppServices.Dispose` is idempotent because both the window and application lifetime can request it.
