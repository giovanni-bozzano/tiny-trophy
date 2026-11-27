# Testing

[README](../README.md) | [Build](BUILD.md) | [Architecture](architecture/README.md)

Testing is split by what can be checked without a window, a network or a running game. Parsing,
merging, view-model state, settings persistence and path rules run anywhere the .NET 10 SDK runs.
Avalonia rendering, Steam HTTP behaviour, the updater and file-system notify overflow need a real
host and are not covered.

## Run

```bash
dotnet test tests/TinyTrophy.Tests.csproj
```

The suite exits non-zero on failure and needs no display, credentials or sample library. Tests work
in temporary directories that are deleted afterwards; they never read the real profile, settings or
achievement files.

## Suites

| File | Covers |
|---|---|
| `AchievementFileParserTests` | JSON object/array, GOG and Epic shapes, INI sections, hex state, UTF-16, `stats.bin`, empty-JSON fallback, truncated files, format priority, fallback folders, file predicates |
| `AchievementMergeTests` | Duplicate merging: union, unlock promotion, earliest unlock time kept, case-insensitive ids, inputs untouched, merge enabled/disabled |
| `AchievementServiceTests` | Cross-source merge, profile totals and completion, hide-zero filter, scanner aggregation |
| `AchievementServiceScanTests` | Single-game scanning and enrichment, failing scanner/enricher containment, progress, cancellation |
| `SettingsServiceTests` | Defaults, round-trip, atomic save, corrupt-file recovery, partial JSON defaults, persisted shape, default-directory merge, concurrent saves |
| `SettingsViewModelTests` | Loading the form, directory add/remove rules, reset-to-defaults, saving and applying, API-key cache invalidation, cache-clear forwarding |
| `SteamEmulatorScannerTests` | Proton glob expansion, relative patterns, default-folder invariants, resolved-directory filtering |
| `SteamOfficialScannerTests` | Key/id gating, owned-game filtering, empty games skipped, progress, mapping |
| `ShadPs4PathTests` | Progress-file and NPWR-id predicates, and the disabled scanner short-circuit |
| `GameWatcherBaseTests` | Mark/diff contract, restart reporting, debounce collapsing, stale runs after stop, event payloads, case-insensitive ids |
| `GameWatcherServiceTests` | Restart aggregation, report-change forwarding, event relay, subscribe-once, dispose |
| `HomeViewModelTests` | Initial load, empty state, queued reload, enrichment gating, sort modes, item formatting, single-game refresh |
| `HomeViewModelRefreshTests` | Refresh during a load, fallbacks, new/removed games, merge, hide-zero, detail page updates, search and sort |
| `GameDetailViewModelTests` | Locked/unlocked split, masking, trophy rarity labels, hidden-achievement rules |
| `GameDetailSortingTests` | Saved sort, all sort modes and labels, unlock ordering, search, refresh, rarity thresholds and colors |
| `MainViewModelTests` | Settings apply, rescan messages, setup flow, navigation, suspend/resume, cache clearing, Steam retry |
| `UpdateServiceTests` | Asset selection, archive rejection, version parsing and ordering |
| `ModelTests` | Completion math, product defaults, API-key warning mapping |
| `GitHubReleaseTests` | Release JSON contract, asset fields and empty defaults |
| `AppPathsTests` | Data and cache roots are absolute, distinct and app-scoped |
| `SmokeTests` | The app paths resolve to usable locations |

## What the suite cannot check

| Needs a display or platform host | Needs network or a real installation |
|---|---|
| Notification window stacking and icon disposal | Steam Web API responses, language cache and offline cooldown |
| Home and detail list virtualization and image lifetimes | Update check, download, binary swap and relaunch |
| Tray menu, window hide/restore and startup entry | File-system notify buffer overflow recovery |
| Theme resources and compiled bindings at runtime | A real game writing achievements mid-scan |

A test that needs one of these is a gap, not a pass. Report it as one.

## Adding tests

- Put shared fakes in `TestSupport.cs`; keep test-specific fakes in the file that needs them.
- Prefer the real class under test with an injected fake over a fake of the class itself. `MainViewModel`
  takes an `IAchievementService`, a settings service and a Steam API service, so its wiring can be
  exercised without a window.
- Use `TempDirectory` for file-system tests. Never point a test at `AppPaths`, the user profile or a
  real game folder.
- `SettingsService` accepts an explicit settings-file path for exactly this reason; keep that seam.
- Tests that change the process working directory run in the non-parallel `CurrentDirectory`
  collection, because CWD is process-global.
- No mocking library, no network, no sleeping longer than a debounce. Poll a condition with a
  timeout instead of asserting on a fixed delay.
- Internal types are visible to `TinyTrophy.Tests` through `InternalsVisibleTo`; prefer a real
  internal call over widening a production API just for a test.

## When a test fails

- A failure names the behaviour it checked. Fix the cause; never weaken an assertion or skip the
  test to get a green run.
- If the production behaviour genuinely has to change, update the test in the same commit and
  explain why in the commit body.
- A test that depends on timing must wait for observed state, not for a guessed duration. If it
  cannot be made deterministic, move the check to a seam rather than adding a retry.
- Never run the suite after a failed build and report it as a result; the binaries are stale.
