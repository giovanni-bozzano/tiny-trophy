# Packaging and updates

[Architecture index](README.md) | [Documentation](../README.md)

## One executable

Release publishes with NativeAOT, full trimming, invariant globalization and size optimization.
The native dependencies Avalonia needs (Skia, HarfBuzz, ANGLE) are embedded as managed resources by
the `EmbedNativeLibraries` target, then extracted to `<cache>/native/<version>` by
`NativeLibraryLoader` at startup and preloaded. The publish output therefore contains the
executable only — no `.dll`, no `.so`, no symbol files.

Constraints this imposes:

- No reflection-only APIs, no runtime code generation, no `Assembly.Load` of app code.
- Every serialized type is registered in `AppJsonContext`.
- Nothing may P/Invoke into Avalonia's native libraries before `NativeLibraryLoader.Initialize`.
- A `Debug` build does not exercise trimming. A Release publish is the real check.

`Directory.Build.props` does not exist here; all publish settings live in `src/TinyTrophy.csproj`.
`Release` is the only configuration with AOT enabled, so `dotnet build -c Debug` stays fast.

## CI

`.github/workflows/publish.yml` runs on pushes to `main` with a two-entry matrix: `windows-latest`
for `win-x64` and `ubuntu-latest` for `linux-x64`, because NativeAOT cannot cross-compile. Each job
sets up .NET 10, installs `clang` and `zlib1g-dev` on Linux, publishes, renames the output to
`tinytrophy_windows_amd64.exe` or `tinytrophy_linux_amd64`, generates build provenance and uploads
the artifact. A separate test job runs the suite on Windows and Linux first, and both publish jobs
depend on it.

## Update flow

1. **Check.** `UpdateService.CheckForUpdateAsync` asks GitHub for the latest release through a
   client with a 10 second timeout, parses the tag as a `Version` and compares it with the
   assembly version. A newer release returns the release object; anything else returns null.
2. **Asset.** `FindAsset` prefers the exact asset name the workflow publishes and otherwise accepts
   a platform suffix, never a `.deb`, `.rpm`, `.zip` or `.tar.gz`.
3. **Download.** The asset streams to `<current-binary>.update` through a separate client with an
   infinite timeout, because `HttpClient.Timeout` also covers reading the body and would abort a
   slow transfer. The caller cancels; progress is reported per chunk.
4. **Swap.** `ApplyUpdateAndRestart` moves the running binary to `.old`, moves the download into
   place, writes `updated.flag` as a marker, releases the single-instance mutex, starts the new
   process and exits.
5. **Notice.** The next launch consumes the flag and shows "Updated to version X" using the version
   it is actually running, not the flag's contents: older updaters wrote the version they were
   replacing. The banner lasts 12 seconds.
6. **Cleanup.** `CleanupPreviousUpdate` deletes `.old` and `.update` leftovers during startup.

If any step fails, the current binary is untouched until the swap begins, and the error is reported
in the update dialog. The user can always re-download the release.

## Versioning

The assembly version in `src/TinyTrophy.csproj` is the source of truth for update comparison; the
release tag must parse after an optional `v`/`V` prefix. Asset names are the workflow's, not typed
by hand, so `FindAsset` and the workflow must change together.
