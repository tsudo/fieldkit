# FieldKit

![License](https://img.shields.io/github/license/tsudo/fieldkit)
![Release](https://img.shields.io/github/v/release/tsudo/fieldkit)
![Downloads](https://img.shields.io/github/downloads/tsudo/fieldkit/total)
![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)

Portable Windows maintenance in a single exe. No installer, no bloat, no telemetry.

FieldKit opens on **Updates**. Check Windows and installed apps together, install the updates you choose, and review the last run. Install apps, Tools & repair, Support kit, and Reports are available in the sidebar.

## Download

Grab the latest `FieldKit.exe` from the [Releases](https://github.com/tsudo/fieldkit/releases) page. See the [changelog](CHANGELOG.md) for what changed.

- Windows 10 or 11, 64-bit
- Run the portable exe from anywhere. To keep it on the PC, open **Support kit → Install FieldKit**. This saves the exe in the displayed support folder (normally `C:\FieldKit`) and adds **Launch FieldKit** to that folder, the shared desktop and the Start menu. Running Install again from a newer portable copy refreshes it.
- Requires administrator privileges at launch

### SmartScreen warning

FieldKit is not code-signed, so Windows SmartScreen may flag it the first time you run it. Click **More info**, then **Run anyway**.

The source is here: read it or build it yourself. Each release's notes give the SHA-256 of its `FieldKit.exe`, which is built by this repository's CI. To check a download, run `Get-FileHash FieldKit.exe` in PowerShell and compare.

## About FieldKit

The **About** page shows the running FieldKit version, disclaimer and MIT license. Choose **Check for FieldKit updates** to compare this build with the latest stable release on GitHub. Open **Releases on GitHub** to review the notes and download a release.

## Command-line updates

From an administrator PowerShell in the installed FieldKit folder, run `.\FieldKit-Update.ps1`. The launcher uses the adjacent FieldKit.exe and the same update service as the GUI. It installs assigned Windows software updates and upgrades eligible apps from the winget source, accepting the relevant licence agreements. Use `-Only Windows` or `-Only Apps` for one source. Use `-WhatIf` to preview the command without contacting either source. Review the printed log path and exit code after a run.

## Updates

Check Windows Update and winget, review the results, then apply all eligible updates or choose individual app updates. The page shows the latest check, update history and restart status. Office Click-to-Run and Microsoft Store actions are under **More update options**.

## Tools & repair

**PC health** reads the registered antivirus, Defender mode and signatures, active firewall profiles, available system-drive space, Windows disk-health and optimization status, BitLocker protection, startup changes and pending restart. Each result has its own status and follow-up action. Hardware or provider data that Windows cannot supply is shown as unavailable. BitLocker recovery keys are never requested.

**Startup review** uses the Microsoft-signed Autorunsc executable from the support kit. Get Autoruns here or from Support kit, then select **Scan startup**. The first full scan establishes a baseline. Subsequent scans compare account, entry, path, publisher and state, with sign-in entries first and scheduled tasks, services and drivers under Advanced. Task Manager provides measured startup impact. If Autorunsc is unavailable, the basic report lists Run keys and startup folders. The full snapshot stays in the administrator-only Recovery folder, outside the support ZIP.

### Cleanup & repair

Choose cleanup and troubleshooting steps as needed. Preview the selection before making changes.

| Group | Step | What it does | Time |
|-------|------|--------------|------|
| Prepare | Create restore point | System restore point before other changes. Says so when Windows skips it because one exists from the last 24 hours | Under 1 min |
| Clean up | Clear temporary files | Removes temp files and folders with nothing written in the last 24 hours; files in use are left alone. In the shared `C:\Windows\Temp` only loose files are removed | Under 1 min |
| Clean up | Empty recycle bin | Deletes what's in the Recycle Bin of the account FieldKit runs as | Under 1 min |
| Clean up | Clear network cache | Flushes the DNS client cache | Under 1 min |
| Repair | Check system files | `sfc /scannow` | 10-30 min |
| Repair | Repair Windows image | `DISM /Online /Cleanup-Image /RestoreHealth` | 10-30 min |
| Repair | Optimize drives | Retrim SSDs, defragment hard disks | 1-5 min |

## Install apps

Choose Firefox, Bitwarden, 7-Zip or VLC individually. The Firefox extension link opens uBlock Origin. **Technician tools** holds Everything and Notepad++. Expand **Keith's picks** for Mullvad VPN, Obsidian, Proton Drive, 1Password, Yubico Authenticator, SumatraPDF, Backblaze, and the official Arq Windows installer. With Arq, create a backup plan and choose its destination after installation.

- Select the apps you want to install and confirm the list.
- Nothing installs until you press the button and confirm the list. winget is told to accept each package's licence and source agreements
- If winget can't say what is installed, apps show "Not checked" instead of being treated as missing
- Each app is installed by exact winget ID from the community `winget` source, one at a time, with its own result
- Apps that winget can't install (browser extensions, account-tied installers, Store-only apps) are listed with a link instead

The catalog is [`FieldKit/Catalog/apps.json`](FieldKit/Catalog/apps.json). It mirrors the author's [recommendations page](https://keithcrawford.me/recommendations/).

## Support kit

Stages troubleshooting tools and a snapshot of the PC in one folder, `C:\FieldKit`. Only administrators can write to it; other users can read and run what's inside.

Building the kit from the portable release also saves the running build as `FieldKit.exe` in that folder. The executable stays available after **Remove kit**.

- **Tools:** Autoruns, Process Explorer, Process Monitor, TCPView, or the full Sysinternals suite. FieldKit downloads them from Microsoft when you build the kit, because the Sysinternals licence does not allow redistribution. Every Windows program in a download must carry a valid Microsoft signature or nothing from that download is kept. Help files and anything else that can't be verified are removed, so the tools come without their `.chm` help.
- **Snapshot reports:** system summary, network configuration, installed apps, startup items, and recent error events, as text files.
- **Zip for sharing** packs the reports and logs (not the tools) for handing to someone else. The reports contain the PC's name, serial number and network addresses; read them before you send them.
- **Remove kit** removes tools, reports, zips, earlier logs and the support-folder shortcut. It preserves the installed application, its launch shortcuts, update launcher and machine preferences.

## Reports

Every maintenance run, app install and kit build is listed with its result, duration and log. System information is available from this page.

## Logging

Logs are written to `C:\FieldKit\Logs\FieldKit-YYYYMMDD-HHMMSS.log`. If the system drive root is not writable, FieldKit uses `%ProgramData%\FieldKit`.

Update checks and installations each write a log and a JSON summary to the Logs folder. Runs from the CLI appear in the same history as GUI runs.

## Machine preferences

**Settings → Network** shows DNS for the selected adapter. Choose Automatic (DHCP), Cloudflare malware filtering, or a NextDNS profile. For NextDNS, use the setup link and enter the six-character profile ID. Apply saves the original DNS configuration, updates the supported IPv4 and IPv6 stacks and tests the selected provider. A failed verification triggers rollback. **Restore previous settings** recovers the original configuration even after switching presets. FieldKit asks for separate confirmation on detected managed or VPN connections. Adapter changes require Windows support for per-interface DNS encryption. Unsupported or unreadable configurations are reported in the page.

**Settings → Storage Sense** shows the affected account, enable state and schedule. Enabling preserves the existing cleanup preferences. Undo restores the previous enable value and registry type, or removes the value if it was originally absent. If another program subsequently changes the value, FieldKit reports the conflict. Recovery records live under the protected Recovery folder and are excluded from the support ZIP.

Install FieldKit or run the update service to create `config.json` in the displayed kit folder (usually `C:\FieldKit`). Edit it as an administrator. The GUI and CLI reload it for updates. Package exclusions use exact winget IDs, case-insensitively. Update logs and summaries are retained for 90 days by default. Set `logRetentionDays` to 0 to keep them indefinitely. The support ZIP includes reports and logs and leaves this configuration out.

```json
{
  "skipAppIds": [],
  "logRetentionDays": 90
}
```

## Limitations

Some Windows maintenance operations are best-effort. Microsoft does not expose one stable API for everything.

- The update runner uses the configured Windows Update source and honors system policy. Updates requiring interactive input may need attention in Windows Settings.
- Store updates depend on a management interface that isn't present on every system.
- Office updates only work for Click-to-Run installs.
- `winget` must already be installed (App Installer). It is a per-user app, so if a different administrator account approved the elevation prompt, FieldKit may not find it. FieldKit shows a notice when it is running as a different account from the one signed in.
- "Silent" installers sometimes still show their own prompts.
- The list of available updates is read from winget's English text output. On a non-English Windows it may come back empty; FieldKit reports a mismatch when it can detect one.
- Per-user steps act on the account FieldKit runs as. If someone else's administrator account approved the elevation prompt, that means their temp files, Recycle Bin and Storage Sense, and FieldKit says so.
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

FieldKit does not collect data or send telemetry. Network operations include Windows Update, Office and Store update checks, winget inventory and package operations, Sysinternals downloads from Microsoft, and the manual FieldKit release check on GitHub.

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
