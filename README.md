# Tiny Trophy

A lightweight achievement tracker that monitors your games across multiple platforms — all in a single, portable executable. Runs natively on both **Windows** and **Linux**, including full support for Steam emulator achievements running under Proton/Wine.

<table>
	<td align="left">
		<img src="assets/home.png" alt="Home" />
	</td>
	<td align="left">
		<img src="assets/game-details.png" alt="Game Details" />
	</td>
</table>

## Features

- **Linux support** — Runs natively on Linux and transparently resolves Steam emulator achievement folders inside Proton/Wine prefixes, with customizable locations
- **Multi-platform support** — Track achievements from Steam, Steam emulators, and PS4 games via ShadPS4
- **Truly lightweight** — Uses ~100 MB of RAM (~10 MB while in background) with a native UI that won't slow down your system while gaming
- **Single portable executable** — No installer, no runtime to pre-install, no dependencies to manage
- **Real-time notifications** — Get desktop popups (with optional sound) the moment you unlock an achievement
- **Live monitoring** — Watches your achievement folders for changes so progress is always up to date
- **Auto-updates** — Checks GitHub for new releases and lets you update in one click

## Why Lightweight Matters

Tiny Trophy is designed to stay out of your way while you game. It uses native desktop technology — no bundled browser engine, no hidden background processes eating into your system resources.

- **~100 MB of RAM (~10 MB while in background)** — Leaves more memory available for your game to use on textures, world streaming, and smoother frame pacing.
- **Minimal CPU usage** — Your processor stays focused on your game, not on rendering a tracker UI in the background.
- **No stutters or frame drops** — Runs silently without competing for GPU composition or system resources while you play.

## Download

Grab the latest release from the [Releases](https://github.com/giovanni-bozzano/tiny-trophy/releases/latest) page.

No installation needed — just download and run.

## Getting Started

1. Download the latest release from the link above
2. Run **TinyTrophy.exe**
3. Follow the initial setup to configure your sources. Enter your Steam API key and Steam ID to track your official achievements
4. Right-click the tray icon to enable **Run on startup** and **Start minimized**

## Supported Sources

| Source | Description |
|--------|-------------|
| Steam (Official) | Your real Steam library via the Steam Web API |
| Steam (Emulator) | Achievement files from Steam emulators (see list below) |
| ShadPS4 | PS4 trophies from the [ShadPS4](https://github.com/shadps4-emu/shadPS4) emulator |

### Supported Steam Emulators

The following emulator folders are detected automatically on first launch:

| Emulator | Default Path |
|----------|-------------|
| Goldberg | `%AppData%\Goldberg SteamEmu Saves` |
| GSE | `%AppData%\GSE Saves` |
| OnlineFix | `%CommonDocuments%\OnlineFix` |
| RUNE | `%CommonDocuments%\Steam\RUNE` |
| CODEX (AppData) | `%AppData%\Steam\CODEX` |
| CODEX (Public Documents) | `%CommonDocuments%\Steam\CODEX` |
| EMPRESS (AppData) | `%AppData%\EMPRESS` |
| EMPRESS (Public Documents) | `%CommonDocuments%\EMPRESS` |
| SmartSteamEmu | `%AppData%\SmartSteamEmu`
| Anadius LSX | `%LocalAppData%\anadius\LSX emu\achievement_watcher` |
| SKIDROW | `%LocalAppData%\SKIDROW` |

### Custom Folders

You can also add your own custom folders from the settings page. Any folder that contains subfolders named by Steam AppID with achievement files will be picked up automatically. Each folder can be individually enabled or disabled.

### Linux Support

Tiny Trophy runs natively on Linux. Since Steam emulator save paths are authored as Windows paths (`%AppData%`, `%LocalAppData%`, etc.), on Linux they are resolved separately inside every Proton/Wine prefix found under your configured **Proton/Wine Prefix Directories** (visible in Settings only on Linux). The following default prefix locations are detected automatically:

| Launcher | Default Prefix Directory |
|----------|--------------------------|
| Steam | `~/.steam/steam/steamapps/compatdata/*/pfx` |
| Steam (alternative) | `~/.local/share/Steam/steamapps/compatdata/*/pfx` |
| Steam (Flatpak) | `~/.var/app/com.valvesoftware.Steam/.steam/steam/steamapps/compatdata/*/pfx` |
| Steam (Flatpak alternative) | `~/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/compatdata/*/pfx` |
| Heroic Games Launcher | `~/Games/Heroic/Prefixes/*` |
| Lutris | `~/Games/*/*` |
| Bottles (Flatpak) | `~/.var/app/com.usebottles.bottles/data/bottles/bottles/*` |

Each `*` is resolved against every real (non-symlink) directory found on disk at that level. You can add, remove, enable, or disable prefix directories from Settings, and use the built-in directory debug panel to see every candidate path each watched folder expands to and whether it exists on disk.

## Why a Steam Web API Key?

Tiny Trophy asks for your personal [Steam Web API key](https://steamcommunity.com/dev/apikey) to fetch achievement names, descriptions, icons, and global unlock percentages directly from Valve's official `ISteamUserStats` API.

Using your own free API key means:

- **Always up to date** — Achievement data comes straight from Steam for every game, including ones released yesterday.
- **Reliable** — Your requests go directly to Valve's servers under your own key, so you're never affected by another user's activity or rate limit.
- **Private and yours** — Your key is stored locally and used only to talk to Valve's servers on your behalf; Tiny Trophy has no server of its own and never sees or transmits your key anywhere else.
- **Free and quick to get** — Generating a key takes less than a minute at [steamcommunity.com/dev/apikey](https://steamcommunity.com/dev/apikey) and doesn't cost anything.

## Building from Source

Requirements: [.NET 10 SDK](https://dotnet.microsoft.com/download)

```bash
git clone https://github.com/giovanni-bozzano/tiny-trophy.git
cd tiny-trophy
dotnet publish src/TinyTrophy.csproj -c Release -r win-x64
```

Replace `win-x64` with `linux-x64` to build for Linux instead.

The output is a single portable executable in `src/bin/Release/net10.0/<rid>/publish/`.

## Acknowledgements

Inspired by [Achievement Watcher](https://github.com/xan105/Achievement-Watcher) by xan105.
