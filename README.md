# FieldKit

![License](https://img.shields.io/github/license/tsudo/fieldkit)
![Release](https://img.shields.io/github/v/release/tsudo/fieldkit)
![Downloads](https://img.shields.io/github/downloads/tsudo/fieldkit/total)
![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)

Portable Windows maintenance in a single exe. No installer, no bloat, no telemetry.

FieldKit runs cleanup, updates, and system health checks from one window. Pick a preset, click run, and get back to work.

## Download

Grab the latest `FieldKit.exe` from the [Releases](https://github.com/tsudo/fieldkit/releases) page. See the [changelog](CHANGELOG.md) for what changed.

- Windows 10 or 11, 64-bit
- No installer — run it from anywhere
- Requires administrator privileges at launch

### SmartScreen Warning

FieldKit is not code-signed, so Windows SmartScreen may flag it the first time you run it. This is normal for unsigned open-source tools.

**To proceed:** Click **More info** then **Run anyway**.

The source code is right here — read it, build it yourself, or check the release against the build instructions below.

## What It Does

12 maintenance operations across four categories:

### Preparation

| Operation | What It Does | Time |
|-----------|-------------|------|
| Create Restore Point | Snapshot before changes | Quick |

### Cleanup

| Operation | What It Does | Time |
|-----------|-------------|------|
| Clear Temporary Files | Removes temp files, skips locked items | Quick |
| Empty Recycle Bin | Clears deleted files permanently | Quick |
| Clear Network Cache | Flushes DNS client cache | Quick |
| Turn On Auto-Cleanup | Enables Storage Sense with monthly settings | Quick |

### Updates

| Operation | What It Does | Time |
|-----------|-------------|------|
| Windows Update | Triggers a Windows Update scan | 1-5 min |
| Update Microsoft Office | Click-to-Run Office updates | Quick |
| Update Store Apps | Microsoft Store update scan | Quick |
| Update Installed Apps | Runs `winget upgrade --all` | 1-5 min |

### System Health

| Operation | What It Does | Time |
|-----------|-------------|------|
| Check System Files | `sfc /scannow` | 10-30 min |
| Repair Windows Image | `DISM /Online /Cleanup-Image /RestoreHealth` | 10-30 min |
| Optimize Drives | Windows drive optimization | 1-5 min |

## Quick Presets

One-click focus modes:

- **All** — full maintenance pass
- **Cleanup** — temp files, recycle bin, DNS, Storage Sense
- **Updates** — Windows, Office, Store, winget
- **Health** — SFC, DISM, drive optimization
- **Defaults** — reset to safe default selections

## How To Use

1. Launch `FieldKit.exe` and approve the UAC prompt
2. Pick a preset or select operations manually
3. Click **Run Selected**
4. Watch the status column and log
5. Review the summary and reboot if recommended

## Safer Defaults

Routine operations are selected at launch. Advanced repair tools (SFC, DISM, drive optimization) are opt-in — you have to select them deliberately.

## Companion Script

FieldKit includes [PC-Maintenance.ps1](PC-Maintenance.ps1), a PowerShell script that runs the same operations from the command line.

- The app is for local, point-and-click use
- The script is for automation, remote sessions, and scheduled tasks

The [PC Maintenance Guide](PC-Maintenance-Guide.md) walks through each operation manually if you want to understand what's happening under the hood.

## Logging

Each run writes a timestamped log to `%TEMP%\FieldKit-YYYYMMDD-HHMMSS.log`. Falls back to the app directory if `%TEMP%` is unavailable.

## Limitations

Some Windows maintenance operations are best-effort — Microsoft doesn't expose a single stable API for everything.

- Windows Update may continue running in the background after the app reports complete
- Store updates depend on a management interface that isn't present on every system
- Office updates only work for Click-to-Run installs
- `winget` must already be installed via App Installer
- Repair operations may recommend a reboot before results are reliable

FieldKit reports these cases clearly in the status column and log instead of failing silently.

## Building From Source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). `global.json` sets `10.0.100` as the minimum; the build uses the newest .NET 10 SDK you have installed.

```bash
dotnet build FieldKit.sln
```

To publish a portable single-file exe:

```bash
dotnet publish FieldKit/FieldKit.csproj -c Release -o publish
```

Output: `publish/FieldKit.exe`

## Privacy

FieldKit does not collect data or send telemetry. Network activity comes only from the Windows maintenance tasks you choose to run (Windows Update, Office update, Store update, winget).

## Disclaimer

This software is provided "as is", without warranty of any kind, express or implied. See the [MIT License](LICENSE) for full terms.

FieldKit performs privileged system operations including deleting files, modifying registry settings, triggering update services, and running system repair tools. These operations modify your system and the results may not be reversible.

**Before running:**
- Back up important files
- Review the selected operations

The author is not responsible for data loss, system instability, or any other outcome resulting from the use of this software. You are responsible for understanding what each operation does before running it.

## License

[MIT License](LICENSE) — Copyright (c) 2026 Keith S. Crawford

## Author

Keith S. Crawford — [@tsudo](https://github.com/tsudo)
