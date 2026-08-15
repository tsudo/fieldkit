# Changelog

All notable changes to FieldKit are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and
this project uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
- The SDK version is pinned in `global.json` so builds are reproducible instead
  of following whatever SDK happens to be newest on the build machine.

### Fixed

- Removed a duplicate `Logger` instance. `MainWindow`'s parameterless
  constructor created its own logger, and the real constructor chained through
  it before replacing the field, leaving an undisposed `StreamWriter` behind.
  The visible symptom was an empty `FieldKit-*.log` file appearing next to the
  exe on every launch.
- Unhandled exceptions on the UI thread are now caught, written to the log, and
  reported with the log path, instead of closing the app without explanation.

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
