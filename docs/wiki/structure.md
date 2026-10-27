# Project structure

Map of the source tree. Describes what each part is for, not how it works.

```
KovaaksCompanion.slnx
src/
  KovaaksCompanion.Core/   logic, no WPF types
  KovaaksCompanion.App/    WPF shell, depends on Core
scripts/                   build-time helpers
docs/                      specs and this wiki
```

## Core

- `Benchmarks/`: playlist definitions, tier and progress calculation, cloud scores, per-account data.
- `Library/`: the run library, play sessions, scenario history, and advice built from them.
- `Stats/`: reading the game's stats CSV files.
- `Input/`: mouse capture (Raw Input), timestamps, sensitivity conversion.
- `Trajectory/`: turning mouse samples into camera paths and per-run motion stats.
- `Perf/`: reading the game's per-run performance files.
- `Replays/`: saving, storing and listing replays; stats-folder watching; storage cleanup.
- `Video/`: game process and window detection, recording, buffering, encoding, webcam overlay.
- `Web/`: opt-in loopback server and userscript that play replays on benchmark websites (token-protected, read-only).
- `Update/`: update check and self-update from GitHub Releases.
- `Migration/`: import of data from older storage layouts.
- `Diagnostics/`: logging and error reporting.
- Root files: settings, UI state, the archive database, cache file helper, atomic file writes, path detection, game launch.

## App

- `MainWindow`, `SettingsView`, `ReplayView`, `Popups`: screens and popup windows.
- Chart and visual controls: `ScoreChart`, `RunChart`, `RadarChart`, `CompareChart`, `InsightCharts`, `ProgressRing`, `RangeBar`, `ActivityCalendar`, `BenchmarkTable`.
- Small UI helpers: scrolling, fonts, colors, backdrop, row click, glyph buttons.
- `AppHost`, `App.xaml`: startup, tray, single instance, autostart.
- `Resources/`: localized strings (English default, Korean).

## Data on disk

Under `%LOCALAPPDATA%\KovaaksCompanion` by default:

- `archive/`: irreplaceable. SQLite database plus per-replay data files.
- `cache/`: disposable, rebuilt on demand.
- Video sessions folder: recorded clips.
