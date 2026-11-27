# Agent instructions

## Stable constraints

- .NET 10 Avalonia desktop app, shipped as a single NativeAOT executable. `Debug` is JIT; `Release`
  enables NativeAOT, full trimming and native-library embedding. Both configurations must build and
  behave the same; the published binary is the only supported artifact.
- C# conventions: tabs, CRLF, file-scoped namespaces, nullable enabled, compiled Avalonia bindings,
  CommunityToolkit.Mvvm source generators. Do not suppress warnings; a clean build has none.
- The UI thread owns everything an Avalonia control binds to. Scanning, parsing, file I/O,
  enrichment, hashing and watcher work stay off it and marshal back through `Dispatcher.UIThread`,
  as `App.StartServices` does.
- A failure in one source stays there. Scanners, enrichers and watchers contain their own
  exceptions and must never stop another source, corrupt shared state or crash the app.
- Watcher callbacks arrive on background threads. Never touch a view model from one. Watchers are
  event driven: do not add polling, timers or busy waits; `GameWatcherBase` already owns debounce,
  restart and stale-run semantics.
- Bitmaps and handles are disposable and have exactly one owner. A control releases the bitmap it
  stops showing; never retain a decoded bitmap after its control unloads, and never dispose one
  still displayed.
- **Keep used memory as low as reasonably possible, within limits that never produce a visible
  performance impact.** The documented footprint is ~100 MB with the window open and ~10 MB while
  in the tray; stay at or below the spirit of that. Use bounded caches, realize only visible rows,
  keep buffers short lived, dispose every `IDisposable`, and do not let event subscriptions or
  static collections retain data. Reclaim memory only at natural boundaries (hiding to the tray,
  the last notification closing), never on a click path, timer or render tick. Never trade
  responsiveness, startup time or disk I/O for a smaller number.
- Persisted state lives under `AppPaths` only (`DataDir`, `CacheDir`, `DataRootDir`). Never
  hard-code a profile path, and never write outside those roots at runtime.
- NativeAOT is the shipping runtime: no reflection-only APIs, no runtime code generation, and any
  new serialized type is registered in `AppJsonContext`.
- Commit every completed logical change separately after validation; do not batch unrelated work or
  include pre-existing user changes. Never push automatically. No AI attribution, co-author
  trailers or "generated with" lines in commit messages.
- No absolute paths, drive letters, machine names or user accounts in code, scripts or docs; take
  paths from the project root.
- Verification leaves nothing behind: no temporary sample data, forced settings, extra logging, or
  stray processes and windows, and never change the user's settings, game library or achievement
  files to test.

## Context and tracking

- Start with `git status --short` and `git log -n 5 --oneline`. Read commit bodies only for
  relevant changes.
- `docs/README.md` is the documentation map. Architecture lives in `docs/architecture/`; load only
  the reference the task needs.
- Use Conventional Commits: `<type>(<scope>): <summary>` (`feat`, `fix`, `refactor`, `docs`,
  `test`, `build`, `chore`). Put the rationale in the body; do not record build or test results,
  deployment state or "not yet run" notes in the message.
- Keep `AGENTS.md` and the skills minimal: replace or remove redundant text before adding. Preserve unchanged wording and section order; no cosmetic rewrites, timestamps, session
  notes or copied tool output.
- Run `build-and-test` after code changes. If something cannot build on this host, build what can
  be built, run the tests it feeds, and report the gap. Docs-only changes need whitespace and
  local-link checks, not a build.
- Keep file responsibilities and public surfaces narrow. Split by responsibility, not line count.

## Code map

| Path | Responsibility |
|------|----------------|
| `src/Program.cs`, `src/App.axaml.cs` | Entry point, single instance, native library loading, tray, startup/shutdown, event wiring |
| `src/Infrastructure/AppServices.cs` | Composition root; builds the object graph and owns disposal |
| `src/Infrastructure/Scanners/` | Produce `Game` lists per source (`SteamEmulator`, `SteamOfficial`, `ShadPs4`) |
| `src/Infrastructure/AchievementFileParser.cs` | Achievement save-file parsing (JSON, INI, `stats.bin`) |
| `src/Infrastructure/Enrichers/` | Add Steam metadata (names, icons, rarities) to scanned games |
| `src/Infrastructure/AchievementService.cs` | Scanning, enrichment, profile totals and duplicate merging |
| `src/Infrastructure/Watchers/` | File-system watches, debounce and unlock detection |
| `src/ViewModels/` | `Main`, `Home`, `GameDetail`, `Settings`, `InitialSetup` state, commands and filters |
| `src/Infrastructure/Images/` | Download-once, resize-once image cache and bitmap ownership |
| `src/Infrastructure/SettingsService.cs` | Atomic settings persistence and default-folder merge |
| `src/Infrastructure/UpdateService.cs` | GitHub release check, download, binary swap and update flag |
| `docs/architecture/` | Design references; load one when needed |
| `tests/` | xUnit suite; no network, no Avalonia host and no real profile data |

## On-demand workflows

Read `.agents/skills/<name>/SKILL.md` when needed:

- `build-and-test`: Debug solution build plus the xUnit suite after code changes; docs-only changes
  need whitespace and link checks instead.

## Verification bar

- `dotnet build TinyTrophy.slnx -c Debug` with zero warnings.
- `dotnet test tests/TinyTrophy.Tests.csproj` green.
- A fix gets a test when the behaviour can be checked without a window, network or clock. When it
  cannot, say so instead of claiming it was verified.
