# FieldKit

![License](https://img.shields.io/github/license/tsudo/fieldkit)
![Release](https://img.shields.io/github/v/release/tsudo/fieldkit)
![Downloads](https://img.shields.io/github/downloads/tsudo/fieldkit/total)
![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)

Portable Windows maintenance in a single exe. No installer, no bloat, no telemetry.

FieldKit runs cleanup, updates and repair steps, installs a short list of trusted apps, and stages a troubleshooting kit on the PC you're working on. One window, four pages.

> This README describes 2.0, which is in preview on `main`. The latest release is still 1.1.0; see the [changelog](CHANGELOG.md).

## Download

Grab the latest `FieldKit.exe` from the [Releases](https://github.com/tsudo/fieldkit/releases) page.

- Windows 10 or 11, 64-bit
- No installer: run it from anywhere
- Requires administrator privileges at launch

### SmartScreen warning

FieldKit is not code-signed, so Windows SmartScreen may flag it the first time you run it. Click **More info**, then **Run anyway**.

The source is here. Read it, build it yourself, or check the release against the build instructions below.

## Maintain

Eleven steps in four groups. Pick a preset or tick steps by hand; they run top to bottom, and each row shows its own result and time. **Stop** ends the current step and skips the rest. **Preview only** reports what each step would do without changing anything.

| Group | Step | What it does | Time |
|-------|------|--------------|------|
| Prepare | Create restore point | System restore point before other changes. Says so when Windows skips it because one exists from the last 24 hours | Under 1 min |
| Clean up | Clear temporary files | Removes temp files and folders with nothing written in the last 24 hours; files in use are left alone. In the shared `C:\Windows\Temp` only loose files are removed | Under 1 min |
| Clean up | Empty recycle bin | Deletes what's in the Recycle Bin of the account FieldKit runs as | Under 1 min |
| Clean up | Clear network cache | Flushes the DNS client cache | Under 1 min |
| Clean up | Turn on auto-cleanup | Enables Storage Sense, monthly | Under 1 min |
| Update | Windows Update | Lists pending updates and opens Settings so you can install them | 1-2 min |
| Update | Microsoft Office | Triggers a Click-to-Run update | Under 1 min |
| Update | Microsoft Store apps | Asks the Store to check for updates | Under 1 min |
| Repair | Check system files | `sfc /scannow` | 10-30 min |
| Repair | Repair Windows image | `DISM /Online /Cleanup-Image /RestoreHealth` | 10-30 min |
| Repair | Optimize drives | Retrim SSDs, defragment hard disks | 1-5 min |

Presets: **Quick tune-up** (the default selection), **Cleanup**, **Updates**, **Deep repair**, **Everything**. Repair steps are never selected by default.

## Apps

Install and update software through `winget`. FieldKit scans once when you open the page, then shows a curated catalog with each app marked as installed, not installed, or update available, plus any other installed apps that have updates.

- Tick what you want, or use a bundle: **Essentials**, **Keith's picks**, **All updates**
- Nothing installs until you press the button and confirm the list. winget is told to accept each package's licence and source agreements
- If winget can't say what is installed, apps show "Not checked" instead of being treated as missing
- Each app is installed by exact winget ID from the community `winget` source, one at a time, with its own result
- Apps that winget can't install (browser extensions, account-tied installers, Store-only apps) are listed with a link instead

The catalog is [`FieldKit/Catalog/apps.json`](FieldKit/Catalog/apps.json). It mirrors the author's [recommendations page](https://keithcrawford.me/recommendations/).

## Support kit

Stages troubleshooting tools and a snapshot of the PC in one folder, `C:\FieldKit`. Only administrators can write to it; other users can read and run what's inside.

- **Tools:** Autoruns, Process Explorer, Process Monitor, TCPView, or the full Sysinternals suite. FieldKit downloads them from Microsoft when you build the kit, because the Sysinternals licence does not allow redistribution. Every Windows program in a download must carry a valid Microsoft signature or nothing from that download is kept. Help files and anything else that can't be verified are removed, so the tools come without their `.chm` help.
- **Snapshot reports:** system summary, network configuration, installed apps, startup items, and recent error events, as text files.
- **Zip for sharing** packs the reports and logs (not the tools) for handing to someone else. The reports contain the PC's name, serial number and network addresses; read them before you send them.
- **Remove kit** deletes the tools, reports, zips, earlier logs and the desktop shortcut.

## Reports

Every maintenance run, app install and kit build is listed with its result, duration and log. System information is available from this page.

## Logging

Logs are written to `C:\FieldKit\Logs\FieldKit-YYYYMMDD-HHMMSS.log`. If the system drive root is not writable, FieldKit uses `%ProgramData%\FieldKit`.

Each run logs a manifest (FieldKit version, Windows build, elevation, uptime, pending reboot, free space, selected steps) and ends with the same summary the app shows.

## Limitations

Some Windows maintenance operations are best-effort. Microsoft does not expose one stable API for everything.

- Windows Update: FieldKit reports what is pending and opens Settings. It does not install Windows updates itself.
- Store updates depend on a management interface that isn't present on every system.
- Office updates only work for Click-to-Run installs.
- `winget` must already be installed (App Installer). It is a per-user app, so if a different administrator account approved the elevation prompt, FieldKit may not find it. FieldKit shows a notice when it is running as a different account from the one signed in.
- "Silent" installers sometimes still show their own prompts.
- The list of available updates is read from winget's English text output. On a non-English Windows it may come back empty; FieldKit reports a mismatch when it can detect one.
- Per-user steps act on the account FieldKit runs as. If someone else's administrator account approved the elevation prompt, that means their temp files, Recycle Bin and Storage Sense, and FieldKit says so.
- Pending Windows updates show as "Check", not "Done", because installing them is still up to you.
- Building the support kit needs an internet connection.
- Repair steps may ask for a restart before their results are reliable.

## Companion script

[PC-Maintenance.ps1](PC-Maintenance.ps1) runs the maintenance steps from the command line, for automation, remote sessions and scheduled tasks. The [PC Maintenance Guide](PC-Maintenance-Guide.md) walks through each one by hand.

## Building from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). `global.json` sets `10.0.100` as the minimum; the build uses the newest .NET 10 SDK you have installed.

```bash
dotnet build FieldKit.sln
```

```bash
dotnet test FieldKit.sln
```

To publish the portable single-file exe:

```bash
dotnet publish FieldKit/FieldKit.csproj -c Release -o publish
```

Output: `publish/FieldKit.exe`. See [CONTRIBUTING.md](CONTRIBUTING.md) for running the UI without the elevation prompt.

## Privacy

FieldKit does not collect data or send telemetry. It makes network requests only for what you start: Windows Update, Office and Store update checks, `winget`, and Sysinternals downloads from `download.sysinternals.com`.

## Disclaimer

This software is provided "as is", without warranty of any kind, express or implied. See the [MIT License](LICENSE) for full terms.

FieldKit performs privileged system operations including deleting files, modifying registry settings, triggering update services, installing software, and running system repair tools. These operations modify your system and the results may not be reversible.

**Before running:**

- Back up important files
- Review what is selected

The author is not responsible for data loss, system instability, or any other outcome resulting from the use of this software. You are responsible for understanding what each operation does before running it.

FieldKit is not affiliated with Microsoft. Sysinternals tools are Microsoft's and are covered by their own licence.

## License

[MIT License](LICENSE). Copyright (c) 2026 Keith S. Crawford

## Author

Keith S. Crawford, [@tsudo](https://github.com/tsudo)
