# Roadmap

FieldKit is moving from a simple maintenance wrapper toward a broader Windows operations utility.

The guiding idea is that the app should stay approachable while becoming more useful for real support and troubleshooting sessions.

## Product Principles

- organize the app around user goals, not just scripts
- support both all-in-one runs and focused single-purpose runs
- prefer safe defaults and visible intent
- keep advanced actions available without making them the default path
- make results easy to understand for non-technical users

## Near-Term Priorities

### 1. Connectivity Module

Add a new operations area for common network troubleshooting.

Candidate operations:

- flush DNS
- show external IP
- ping default gateway
- ping public DNS targets such as `1.1.1.1` and `8.8.8.8`
- resolve DNS for a hostname
- run `tracert`
- basic adapter and IP summary export

Why:

- this fits the same "fix the PC" use case
- it expands the app beyond maintenance into support diagnostics
- it creates a strong reason to use the app for one focused task

### 2. App Install Bundles

Add a workflow for installing or updating a core app set.

Candidate features:

- create a Ninite URL or bundle for common app installs
- install a curated set of apps through `winget`
- export a selected app bundle for reuse
- maintain profiles such as `Basic PC`, `Workstation`, `Creator`, or `Recovery`

Why:

- it complements maintenance and update workflows
- it supports new machine setup and repair scenarios
- it gives the app a more complete "PC operations" identity

### 3. Better Results And Summaries

Make outcome reporting more visual and easier to scan.

Candidate improvements:

- show last run results in dashboard cards
- persist recent operation history
- store warnings and reboot-required states in a clearer summary view
- add per-operation result notes in the UI

### 4. Diagnostics Export

Make it easier to hand results to another person.

Candidate improvements:

- export a run summary
- export diagnostics plus system info
- create a support bundle with log and machine details

## Mid-Term Direction

### Preset-Driven Workflow

Presets should become a first-class concept.

Planned preset ideas:

- Full Maintenance
- Cleanup Only
- Updates Only
- System Health
- Connectivity Check
- New PC Setup

### Operations Library

The app should grow into a library of operations that can be mixed into purpose-built presets.

That means the user experience should answer:

- what kind of work are you trying to do
- what should run for that goal
- what happened when it finished

## UI Direction

The current operations-list layout is the base direction.

Future UI goals:

- keep the main screen clean and dashboard-like
- make presets and operation focus obvious
- show useful summaries without forcing the user into separate screens
- reduce the "developer tool" feel in favor of a polished desktop utility feel

## Notable Open Questions

- Should connectivity tests live beside maintenance tasks or in a separate module view?
- Should app bundle install workflows use Ninite, winget, or both?
- Should the app store saved presets locally?
- Should results/history persist across runs?

## Out Of Scope For Now

- remote management
- background services
- silent enterprise deployment features
- cloud accounts or telemetry

The short-term goal is a stronger local desktop utility, not an RMM platform.
