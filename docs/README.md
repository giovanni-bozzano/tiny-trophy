# Documentation

Tiny Trophy's documentation is split by what a reader is trying to do. Read the entry that matches
the task, then follow its links instead of reading everything.

## Start here

| I want to... | Read |
|---|---|
| See what Tiny Trophy tracks and why it is light | [README](../README.md) |
| Build, run or publish it | [Build and publish](BUILD.md) |
| Run or add tests | [Testing](TESTING.md) |
| Understand how the pieces fit | [Architecture](architecture/README.md) |
| Follow the rules for changing code | [AGENTS](../AGENTS.md) |

## Guides

| Guide | Scope |
|---|---|
| `BUILD.md` | SDKs, Debug build, run, NativeAOT publish and outputs |
| `TESTING.md` | Test suite map, running tests, what needs a window or network |

## Architecture

`architecture/` explains why the code is shaped the way it is. It is not an API reference: types,
constants and field lists live in `src/`, and the code is the final authority.

| Reference | Answers |
|---|---|
| [Overview](architecture/Overview.md) | Components, data flow and ownership |
| [Achievement sources](architecture/Achievement-Sources.md) | How each source is scanned, parsed, merged and watched |
| [Threading and lifecycle](architecture/Threading-and-Lifecycle.md) | UI thread, background work, watchers, tray and shutdown |
| [Persistence and caching](architecture/Persistence-and-Caching.md) | Settings, Steam metadata cache, images and memory |
| [Packaging and updates](architecture/Packaging-and-Updates.md) | NativeAOT, CI artifacts and the update flow |

## How the docs are kept

- **One owner per fact.** Usage belongs in `README.md`, build and publish steps in `BUILD.md`,
  verification in `TESTING.md`, and design rationale in `architecture/`. Link instead of copying.
- **No build-specific numbers in prose.** Do not pin package versions, file sizes or tunable
  constants. Name the type, method or setting instead.
- **Explain the present.** Describe how the system works, not the bugs it fixed. Keep historical
  rationale only where it states a constraint a future change could otherwise violate.
- **Graphs are Mermaid.** Use a `mermaid` fenced block for a diagram; renderers draw it and the
  source stays diffable.
- **Update the owner, not every copy.** When behaviour changes, change the guide that owns it and
  fix the links that point at it.

Coding agents should start with the repository root, then `architecture/README.md` for design
context, and load only the reference a task needs. Read `git status --short` and recent commits
before planning work.
