# Contributing

Pull requests are welcome.

## Before Opening a Pull Request

1. Keep changes scoped to one concern.
2. Follow the product principles in [ROADMAP.md](ROADMAP.md) — approachable by default, advanced actions available but not the default path, safe defaults over convenience.
3. Build and run locally before submitting: see [Building From Source](README.md#building-from-source).
4. New maintenance operations should state what they do and roughly how long they take, matching the existing table format in the README.

## Working on the UI

FieldKit asks for administrator rights at launch. For layout work you can skip the prompt:

```bash
dotnet run --project FieldKit -p:FieldKitNoElevate=true --no-incremental
```

`--no-incremental` matters: the manifest is embedded at compile time, and an incremental build keeps whichever one was compiled last. For the same reason, rebuild with `--no-incremental` before publishing if you have used this switch. CI fails a publish that lacks the administrator manifest.

Steps that need administrator rights fail in this mode. Debug builds also accept `--capture file.png`, which renders the window to a PNG and exits; add `--page apps` (or `kit`, `reports`) to choose the page.

Colours and control styles live in `FieldKit/Themes/Theme.xaml`. Don't set a hex colour anywhere else.

## Tests

```bash
dotnet test FieldKit.sln
```

Logic that decides a step's result belongs in a static method the tests can call, as `SfcTask.Interpret` and `WingetService.ParseUpgradeTable` do.

## Reporting Issues

Use [GitHub Issues](https://github.com/tsudo/fieldkit/issues) for bugs, and check [SECURITY.md](SECURITY.md) first if it's a vulnerability rather than a bug.
