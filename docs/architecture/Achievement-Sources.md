# Achievement sources

[Architecture index](README.md) | [Documentation](../README.md)

Three sources feed one list. Each has its own scanner, its own definition of "a game" and, for the
local sources, its own watcher. They share the parser, the enricher and the merge rules.

## Steam emulators

`SteamEmulatorScanner` and `SteamEmulatorWatcher` read save folders written by Goldberg, GSE,
OnlineFix, RUNE, CODEX, EMPRESS, SmartSteamEmu and similar. A watched directory is a folder whose
children are named by Steam AppID:

```text
<watched-directory>/<app-id>/achievements.json
<watched-directory>/<app-id>/stats/achievements.ini
```

- Default folders are declared with portable tokens (`%AppData%`, `%CommonDocuments%`,
  `%LocalAppData%`, `%UserProfile%`), never with a machine-specific path.
- On Linux, each token is resolved inside every configured Proton/Wine prefix, so the same save
  layout works under Proton. A prefix glob is expanded against real directories only.
- A folder is a game when the app ID directory exists and at least one parsed achievement comes
  back. A directory with unrecognized files is not listed.
- When the same AppID exists in several watched folders, the watcher merges their unlocks so a
  change in any copy is seen.

The parser tries, in order: `achievements.json`, `user_stats.ini`, `achievements.ini`, `stats.bin`,
then the same names in `stats/`, `Stats/`, `SteamData/`, `SteamEmu/` and `SteamEmu/UserStats/`.
`achievements.json` covers the object, array, GOG and Epic shapes. Container values and metadata
objects are skipped; a recognized key holding an object is not treated as an achievement.

## Steam official

`SteamOfficialScanner` uses the Steam Web API and requires both an API key and a Steam ID. Without
either it returns nothing and makes no request.

1. `GetOwnedGames` lists the library.
2. `GetPlayerAchievements` runs per game with bounded concurrency.
3. Games with no achievements are skipped; the rest become `AppID: <id>` entries until enrichment
   replaces the name.

The source can be disabled in Settings. Its achievements are identified by API names, which is also
what the metadata schema uses, so enrichment matches them directly.

## ShadPS4

`ShadPs4Scanner` and `ShadPs4Watcher` read the emulator's trophy data under the app data root:

```text
<app-data>/shadPS4/trophy/<NPWR>/Xml/TROP_01.xml     trophy definitions
<app-data>/shadPS4/trophy/<NPWR>/Icons/TROP001.PNG   icons
<app-data>/shadPS4/home/<user>/trophy/<NPWR>.xml     per-user progress
```

- A game exists when its trophy XML parses and declares at least one trophy.
- Trophy metadata comes from the English base file (`TROP_01.xml`, then `TROP.xml`); progress is
  merged across every user file, keeping the latest unlock time.
- Trophy types are stored lowercase (`p`, `g`, `s`, `b`) and drive the Platinum/Gold/Silver/Bronze
  labels.
- The watcher filters progress files by path (a `trophy` directory under `shadPS4/home`) and reads
  the NPWR id from the file name; trophy-definition changes and progress changes are both watched.

## Merging

`AchievementService.MergeGames` groups by `AppId`. The first entry of a group is copied and becomes
the primary; later entries contribute achievements by id (case-insensitive):

- An achievement missing from the primary is added.
- A locked achievement that is unlocked elsewhere is promoted, taking that unlock time.
- An already-unlocked achievement keeps its state and time.
- The inputs are never mutated, so the next rebuild can merge them again.

Merging is disabled by `Achievements.MergeDuplicate`; the group then returns the original list.
Profile totals count the merged list, and `HideZeroPercent` removes games with no unlocks from both
the list and the totals.
