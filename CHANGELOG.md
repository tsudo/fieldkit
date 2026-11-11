# Changelog

All notable changes to FieldKit are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and
this project uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.0.1] - 2026-11-11

### Security

- The support kit's signature check now requires the signing certificate to
  chain to one of Microsoft's own roots. Before, a valid signature in the name
  "Microsoft Corporation" was enough, which any root trusted by the PC could
  have issued.

### Changed

- `System.Management` and `System.ServiceProcess.ServiceController` updated
  from 10.0.11 to 10.0.15. Dependabot is told to skip major versions of these
  two, so the app stays on the .NET 10 LTS line it ships its runtime from.
- Test packages updated: xunit 2.9.3, xunit.runner.visualstudio 4.0.1,
  Microsoft.NET.Test.Sdk 18.11.1.

## [2.0.0] - 2026-11-11

FieldKit becomes a four-page app: Maintain, Apps, Support kit and Reports.

### Added

- **Apps page.** Installs and updates software through winget from a curated
  catalog (`FieldKit/Catalog/apps.json`), with bundles and a list of other
  installed apps that have updates. Nothing installs before a confirmation that
  names every change.
- **Support kit page.** Downloads selected Sysinternals tools from Microsoft,
  verifies each program's Microsoft signature, and writes text snapshots of the
  PC (system, network, installed apps, startup items, recent errors) into
  `C:\FieldKit`. Zip for sharing and Remove kit are included.
- **Reports page.** Lists every maintenance run, app install and kit build with
  its result, duration and log.
- **Stop button** on every long-running job. The current step is ended and the
  rest are marked skipped.
- Per-step status, elapsed time and result on each row, and a summary card in
  the window instead of a message box.
- A notice when FieldKit is running as a different account from the one signed
  in at the desktop.
- Unit tests (`FieldKit.Tests`) for winget parsing, the catalog, and task
  result logic. CI runs them.
- `--page maintain|apps|kit|reports` opens FieldKit on that page.

### Security

- The kit folder is created owned by Administrators with a protected ACL (SYSTEM
  and Administrators full control, Users read-only). FieldKit runs elevated and
  deletes in this folder, so a standard user must not be able to plant a link
  in it. A folder that already exists and is not owned by Administrators or
  SYSTEM, or that anyone else can write to, is never adopted or repaired: it is
  renamed to `FieldKit.untrusted-<timestamp>` with its contents untouched, and
  a fresh folder is created.
- Subfolders are checked for links again at each use, the zip walk never
  follows a link, and a link found where the log folder should be is removed as
  a link without touching its target.
- Clear temporary files no longer deletes folder trees in `C:\Windows\Temp`.
  Any user can write there, and a folder swapped for a junction partway through
  a delete would have had its target emptied with administrator rights. Only
  loose files are removed from the shared temp; folder trees are removed only
  from the elevated account's own temp. This also affected 1.x.
- Downloaded Sysinternals archives are filtered by content. Every Windows
  program in the archive, whatever its extension, must be Microsoft-signed or
  the download is rejected; `.txt` files are kept; everything else (help files,
  scripts, shortcuts) is removed.
- winget is run only when its alias is the reparse point Windows creates.
  PowerShell steps load modules only from the PowerShell install folder, and
  temp folders come from known-folder paths rather than environment variables.
- Closing FieldKit during a run now stops the job and waits for its summary.
  Before, the window closed and DISM or an installer carried on unwatched.
- Releases can only be built with the administrator manifest: the development
  switch that removes it applies to Debug builds alone.

### Changed

- One theme file (`FieldKit/Themes/Theme.xaml`) now holds every colour and
  control style. The task grid is replaced by a grouped checklist.
- Logs moved from `%TEMP%` to `C:\FieldKit\Logs`.
- App updates moved from a mid-run dialog on the Maintain page to the Apps page,
  so a maintenance run no longer waits on a dialog.
- Windows Update now asks the Windows Update Agent what is pending, lists it,
  and opens Settings. It no longer calls the undocumented `UsoClient`.
- Clear temporary files leaves anything changed in the last 24 hours alone.
- Folders, files and links are opened through Explorer rather than started from
  the elevated process.
- PowerShell steps are passed with `-EncodedCommand`.

### Fixed

- Create restore point reported success when Windows silently skipped it (one
  already existed from the last 24 hours), and treated any output containing
  "24" as that case. It now compares restore point sequence numbers.
- Check system files could never read its own result: `sfc` writes UTF-16 when
  redirected and was decoded as a single-byte encoding, so every run fell
  through to "SFC completed".
- Clear temporary files returned a warning on nearly every run because the open
  log file in `%TEMP%` counted as a locked item, and each run deleted the logs
  of earlier ones.
- winget's update list was parsed by fixed column offsets, which broke on
  non-Latin app names and when winget omitted the Source column. Rows are now
  read from the right, and package IDs are validated before use. Upgrades pass
  `--exact`.
- winget output was decoded with the wrong encoding.
- The licence dialog showed "v1.0" regardless of version.
- A failed winget scan looked like a clean one. Exit codes are now read, an
  unknown installed state shows as "Not checked" instead of "not installed",
  and FieldKit says so when winget announces more updates than it could read.
- Results that claimed too much: pending Windows updates, a Store request the
  Store refused, and a Recycle Bin emptied for the administrator account rather
  than the signed-in user now show as "Check", not "Done". A failed event-log
  query is no longer written up as "no errors", and a timeout is no longer
  labelled as the Stop button.
- A temp folder was judged by its own timestamp, which only changes when its
  direct children do. Its age is now the newest write anywhere inside it.
- A row could stay at "Running" after an unexpected failure, and the Apps page
  could stay on "Checking" for the whole session.
- The Recycle Bin step treated any error text containing "empty" as "already
  empty".

### Removed

- `WingetUpdateWindow` and the "Update Installed Apps" maintenance step, both
  replaced by the Apps page.

## [1.1.0] - 2026-08-15

Maintenance release. No new operations — this moves the runtime off a version
heading for end of support and fixes two defects found during review.

### Changed

- Runtime moved from .NET 9 to .NET 10 (LTS). .NET 9 reaches end of support on
  2026-11-10; because FieldKit publishes as a self-contained single file, the
  runtime is embedded in the exe, so shipping on .NET 9 past that date would
  hand every downloader an unpatched runtime. .NET 10 is supported through
  2028-11-14.
- `System.Management` and `System.ServiceProcess.ServiceController` updated from
  9.0.0 to 10.0.11.
- `global.json` sets .NET 10 as the minimum SDK, so a build on an older SDK
  fails immediately with a clear message. It is a floor rather than a pin by
  design: the SDK in use determines which runtime version is embedded in the
  self-contained exe, and a self-contained app only receives runtime fixes when
  it is rebuilt, so tracking forward is the safer default.

### Fixed

- Removed a duplicate `Logger` instance. `MainWindow`'s parameterless
  constructor created its own logger, and the real constructor chained through
  it before replacing the field, leaving an undisposed `StreamWriter` behind.
  The visible symptom was an empty `FieldKit-*.log` file appearing next to the
  exe on every launch.
- Unhandled exceptions on the UI thread are now caught, written to the log, and
  reported with the log path, instead of closing the app without explanation.
- A run that stops early now says so. Previously the status bar read "Run
  complete" at 100% whether or not the run finished; an interrupted run reports
  "Run aborted", marks the operations that did not finish, and shows what went
  wrong instead of a completion summary.
- Closing the window mid-run no longer disposes the cancellation token while the
  run loop is still using it. Every remaining operation used to fail with
  `ObjectDisposedException`, which read in the log as many broken operations
  rather than one lifecycle bug.

### Added

- CodeQL static analysis, running on every push and pull request to `main` plus
  a weekly schedule.
- Continuous integration now verifies the published single-file artifact, not
  just `dotnet build`. Single-file packing, self-contained runtime bundling, and
  compression have their own failure modes that a plain build never exercises.
- Issue and pull request templates.
- This changelog.

## [1.0.0] - 2026-04-06

First release.

### Added

- 12 maintenance operations across cleanup, updates, and system health.
- Presets for focused runs: All, Cleanup, Updates, Health, Defaults.
- WPF interface with a Fluent dark theme.
- System Information panel with export.
- Timestamped log file for every run.
- Security hardening: absolute paths for system binaries, and symlink and
  junction protection on temp cleanup.

[1.1.0]: https://github.com/tsudo/fieldkit/releases/tag/v1.1.0
[1.0.0]: https://github.com/tsudo/fieldkit/releases/tag/v1.0.0
