# Build and publish

[README](../README.md) | [Testing](TESTING.md) | [Architecture](architecture/README.md)

## Prerequisites

- The .NET 10 SDK.
- Windows x64 or Linux x64. NativeAOT cannot cross-compile, so each runtime identifier is
  published on its own OS.
- NativeAOT's C toolchain: the MSVC build tools on Windows, `clang` and `zlib1g-dev` on Linux.
- Nothing else: Avalonia and CommunityToolkit.Mvvm restore from NuGet, and the published
  executable carries its native dependencies.

## Build

```bash
dotnet build TinyTrophy.slnx -c Debug
```

Debug is a normal JIT build: fast, debuggable, with Avalonia's developer tools. It produces the
app and the test project. There is no installer and no platform-specific build step.

## Run

```bash
dotnet run --project src/TinyTrophy.csproj
```

The app is single-instance (a named mutex) and lives in the system tray. A second launch exits
immediately, and closing the window only hides it; use the tray menu's **Exit** to quit. Do not
assume a fresh process when a previous run is still in the tray.

## Tests

```bash
dotnet test tests/TinyTrophy.Tests.csproj
```

No display, network or installed game is needed. [Testing](TESTING.md) maps the suites and the
gaps.

## Publish

```bash
dotnet publish src/TinyTrophy.csproj -c Release -r win-x64
```

Replace `win-x64` with `linux-x64` on Linux. Output: one executable in
`src/bin/Release/net10.0/<rid>/publish/`.

| Setting | Effect |
|---|---|
| `PublishAot` | Compiles to native code; no runtime installed on the target |
| `TrimMode=full` | Removes unused code; anything reflection-only breaks at publish or runtime |
| `InvariantGlobalization` | Drops ICU; culture-specific formatting is not available |
| `OptimizationPreference=Size` | Favors a smaller binary over peak code speed |
| `EmbedNativeLibraries` | Packs Skia, HarfBuzz and ANGLE into the executable |

`Debug` skips all of this, so a `Release` publish is the only real check that trimming and AOT
still work. `NativeLibraryLoader` extracts the embedded libraries into the cache directory before
Avalonia starts; nothing may P/Invoke into them earlier.

CI (`.github/workflows/publish.yml`) publishes both runtime identifiers on their own runners,
renames the binaries to `tinytrophy_windows_amd64.exe` and `tinytrophy_linux_amd64`, attests them
and uploads the artifacts. The Linux job installs `clang` and `zlib1g-dev` first. The workflow runs
on pushes to `main`, runs the test suite on Windows and Linux first, and only then starts
both publish jobs; a failed test blocks every artifact.

## When a build or publish fails

- **`dotnet build` fails after a package change.** Restore first, then check that the new API
  exists in the Debug build; trimming problems only appear in Release.
- **AOT publish fails with `IL2xxx`/`IL3xxx`.** Something uses reflection, a dynamic code path or
  an unregistered serializer. Fix the code or register the type in `AppJsonContext`; do not turn
  trimming off.
- **Publish succeeds but the app exits immediately.** Another instance is in the tray, or a stale
  `.old`/`.update` file sits beside the executable. Exit the tray instance and relaunch.
- **The app starts but images do not load.** The image cache is under the platform cache
  directory; a locked or read-only cache falls back to downloading, which needs network access.
- **The published binary is missing.** It is written next to the runtime identifier under
  `src/bin/Release/net10.0/`, not to the repository root.

## App data locations

| What | Where |
|---|---|
| Settings, update flag | `%AppData%\tinytrophy` on Windows, `$XDG_CONFIG_HOME/tinytrophy` or `~/.config/tinytrophy` on Linux |
| Steam metadata, images, extracted natives | `%LocalAppData%\tinytrophy` on Windows, `$XDG_CACHE_HOME/tinytrophy` or `~/.cache/tinytrophy` on Linux |
| Autostart entry (Linux) | `$XDG_CONFIG_HOME/autostart/TinyTrophy.desktop` |

The cache directory is safe to delete while the app is closed; settings are not.
