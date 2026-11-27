# Persistence and caching

[Architecture index](README.md) | [Documentation](../README.md)

## Roots

`AppPaths` resolves the only two roots the app may write to:

| Root | Windows | Linux | Holds |
|---|---|---|---|
| `DataDir` | `%AppData%\tinytrophy` | `$XDG_CONFIG_HOME/tinytrophy` or `~/.config/tinytrophy` | `settings.json`, `updated.flag` |
| `CacheDir` | `%LocalAppData%\tinytrophy` | `$XDG_CACHE_HOME/tinytrophy` or `~/.cache/tinytrophy` | Steam metadata, images, extracted natives |
| `DataRootDir` | `%AppData%` | `$XDG_CONFIG_HOME` or `~/.config` | Autostart entries and the ShadPS4 data root |

On Linux the XDG variables are read explicitly because `Environment.SpecialFolder` can be empty
when `HOME` is unset; the app fails loudly rather than writing relative to the launch directory.
The cache directory is disposable; the data directory is not.

## Settings

`settings.json` is read once at startup and written by `SettingsService`:

- The file is serialized from a copy, written to `settings.json.tmp` and moved into place, so a
  crash cannot truncate the live file.
- A save gate serializes concurrent writers (watchers can trigger a save while the user does).
- An unreadable or corrupt file is renamed to `settings.json.corrupt` instead of being overwritten,
  and defaults are used.
- Only custom folders and explicitly disabled default folders are persisted. Defaults are re-added
  on every load, so a new version's folders appear and a disabled default stays disabled by path.
- The in-memory settings object is the live source of truth for scanners and watchers; saving never
  mutates it into its persisted shape.

## Steam metadata cache

`SteamApiService` keeps metadata in memory and on disk:

- One entry per app id, tagged with the language it was fetched in. A language change invalidates
  the entry instead of serving stale wording.
- One gate per app id, so concurrent lookups of the same game fetch once. The gate is removed when
  idle. A key change drops the whole cache through `ClearCache`; a language change invalidates the
  affected entries, which are rebuilt on their next lookup.
- Disk cache files are written aside and moved into place. A cached entry missing achievements that
  Steam now reports is rebuilt from scratch; otherwise global percentages are refreshed in place.
- A connection failure starts a 30 second offline cooldown, so being offline does not cost a
  timeout per game. A successful request ends it early.
- The warning banner's **Retry** action clears the cooldown and reloads, so a user who just
  reconnected does not have to wait it out or wipe the metadata cache.
- `ClearCache` drops memory and deletes disk files, including interrupted `.tmp` writes.

## Images

`ImageLoaders` owns two `CachedImageLoader` instances: game covers at 320 px and achievement icons
at 120 px (2x their drawn width for HiDPI). Each loader:

- Joins an in-flight download for the same URI instead of starting a second one.
- Resizes once, right after download, and stores PNG (transparency matters for icons).
- Names cache files by the SHA-256 of the URI, writes `.partial` and moves into place, and deletes
  stale partial files on startup.
- Returns a lease whose bitmap belongs to the control that asked for it. Nothing keeps decoded
  bitmaps: lists realize only visible rows, so the app holds roughly a screenful.

`SizedImageLoader` cancels a superseded load and disposes a lease that arrives after cancellation.
`ImageSourceDisposer` disposes the bitmap when the `Image` unloads, detaches, or replaces its
source. Both exist because a decoded bitmap lives in unmanaged memory the GC barely accounts for.

## Update flag

`updated.flag` records the version installed by the updater. It is written after the binary swap and
consumed once at the next startup, which shows a temporary banner and deletes the file. An empty or
unreadable flag is ignored, and the write is best effort: failing to record it must not fail an
update.

## Memory rules that follow from this

- Keep caches on disk; do not add an in-memory bitmap or achievement cache to speed up scrolling.
- Dispose leases, streams and `Bitmap` instances in `finally` or with `using`.
- Do not subscribe long-lived objects to events without unsubscribing; static events and static
  collections must not retain pages or games.
- Reclaim memory only at natural idle boundaries (tray hide, last notification closed). A GC or
  working-set trim in a click, scroll or render path is a regression, even if the number looks
  better.
