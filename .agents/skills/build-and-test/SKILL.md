---
name: build-and-test
description: Build the Debug solution and run the xUnit suite after code changes.
---

Run from the repository root:

```bash
dotnet build TinyTrophy.slnx -c Debug --nologo
if [ "${PIPESTATUS[0]}" -ne 0 ]; then echo "build failed"; exit 1; fi
dotnet test tests/TinyTrophy.Tests.csproj --nologo
```

Do not run the tests after a failed build: the binaries are stale. Do not claim a UI, update or
network path was verified by this suite; it has no display, network or installed game.

Report the build result, the observed pass/fail counts and any coverage limit in three lines or
fewer; expand only for an actionable failure. For docs-only changes, skip the build and check
whitespace plus affected local links instead. Leave no temporary files, forced settings or
processes behind.
