<div align="center">

# QinmoLauncher

**A Windows desktop Minecraft launcher built on the Trident core.**

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg?style=for-the-badge)](https://opensource.org/licenses/MIT)
[![.NET 10](https://img.shields.io/badge/.NET-10-5C2D91?style=for-the-badge)](https://dotnet.microsoft.com/)
[![Avalonia](https://img.shields.io/badge/Avalonia-12-3355FF?style=for-the-badge)](https://avaloniaui.net/)

[中文介绍](README.zh.md) • [Releases](https://github.com/reqinme/QinmoLauncher/releases) • [Report a bug](https://github.com/reqinme/QinmoLauncher/issues)

</div>

---

## What this is

QinmoLauncher is a Windows-only desktop launcher: every Minecraft version and the
loaders that go with it, Microsoft and offline accounts, and one isolated workspace per
instance.

It is a renamed fork of [Polymerium](https://github.com/d3ara1n/Polymerium). The
interface, the theming and the engine are Polymerium's work; this fork keeps that
foundation and changes the handful of things listed below. Attribution is in
[NOTICE](NOTICE).

The engine is **Trident** ([submodules/Trident.Net](submodules/Trident.Net)), a
declarative instance toolchain that also powers a standalone `trident` CLI and an MCP
server. The launcher is a shell over that engine: it drives the same managers, reads
the same `profile.json`, and writes the same on-disk layout.

## What it does differently

- **Deploys without Developer Mode.** Trident expresses an instance's `build/`
  directory as links into the shared cache. Creating symlinks needs a privilege
  ordinary Windows users do not hold, which used to make deployment fail outright. This
  fork falls back to hard links and junctions, so a normal install works.
- **Configurable download source.** An opt-in mirror with automatic fallback to the
  origin, plus adjustable parallelism and request timeout.
- **Crash reports go to GitHub Issues.** No third-party crash service and no telemetry.
  A report is a prefilled issue URL you choose to open, never a background upload.
- **The Chinese interface is actually Chinese.** The startup language is matched
  against the shipped resource set, so a `zh-CN` Windows no longer silently falls back
  to English.
- **Instance data stays in its own directory** instead of sharing the engine-wide one
  that other Trident front ends also write to. See [Data locations](#data-locations).
- **Deployment failures say why.** A batch that fails reports the reason per item
  rather than a bare count.

## Requirements

- Windows 10 or 11, x64
- No Developer Mode, no symbolic-link privilege, no administrator rights

## Install

Releases are built by CI from a `v*` tag. Until the first release is published, build
from source:

```powershell
git clone --recurse-submodules https://github.com/reqinme/QinmoLauncher.git
cd QinmoLauncher
dotnet restore
dotnet build "Polymerium.slnx"
```

Then run:

```powershell
.\src\Polymerium.Avalonia\bin\Debug\net10.0\QinmoLauncher.exe
```

The .NET 10 SDK version is pinned by `global.json` (currently `10.0.401`).

## Data locations

Everything this launcher owns lives in one directory, deliberately separate from the
engine-wide directory that other Trident front ends write to:

| What | Where |
|---|---|
| Data root | `%LOCALAPPDATA%\QinmoLauncher\Trident` |
| Instances | `<root>\instances\<key>\` |
| Shared cache (assets, libraries, runtimes, packages) | `<root>\cache\` |
| Settings, accounts, HTTP cache, crash reports | `<root>\.qinmolauncher\` |
| Accounts used by the bundled CLI | `<root>\.trident.cli\` |

An explicitly set `TRIDENT_HOME` overrides the root and everything above moves under
that path instead. This matters when running the bundled `trident` CLI by hand, since
the CLI does not apply the launcher's default:

```powershell
$env:TRIDENT_HOME = "$env:LOCALAPPDATA\QinmoLauncher\Trident"
```

## Architecture

A thin Avalonia shell over the Trident core. The shell contributes the MVVM
page/dialog/modal/toast experience, theming, local persistence and self-update; it does
not re-implement instance management, deployment, repositories, accounts or
import/export. Those belong to the core.

An instance is **declarative**. `profile.json` states what the instance should be —
game version, loader, packages, rules — and a staged deployment pipeline turns that
into a runnable directory:

```
instances/<key>/
  profile.json    the declaration
  import/         modpack source files (real copies)
  persist/        local data that survives redeploys
  build/          the runnable directory, linked into cache/ and persist/
  snapshots/      snapshots
```

`build/` mostly holds links rather than copies, which keeps instances cheap to create
and rebuild. Because the declaration is the single source of truth, an instance can
always be rebuilt from `profile.json`.

### Project structure

```
src/Polymerium.Avalonia/    the desktop shell
submodules/Trident.Net/     the engine: Abstractions <- Pref <- Core <- Cli
```

## Platform support

**Windows x64 only.** This fork is built and tested on Windows only. The upstream
project also ships Linux and macOS builds; this fork does not.

## Privacy

No telemetry, no analytics, no third-party crash reporting.

When the launcher catches an unhandled error it can assemble a report. Nothing is sent
unless you open the prefilled GitHub issue yourself, and the payload is yours to review
first.

## License

MIT. See [LICENSE.txt](LICENSE.txt).

This project is derived from Polymerium (Copyright (c) d3ara1n) and consumes
Trident.Net as a submodule. Both are MIT-licensed; the required attributions are in
[NOTICE](NOTICE).
