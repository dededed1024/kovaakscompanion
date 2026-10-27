# AGENTS.md

Guide for AI coding agents and human contributors. Structure and rules only; read the code for behavior.

## Project

KovaaK's Companion: a Windows tray app (.NET 10, WPF, x64) that tracks KovaaK's benchmark progress and records runs for replay. It never hooks, injects into, or reads memory of the game.

## Layout

- `src/KovaaksCompanion.Core`: logic with no WPF types. Everything testable lives here.
- `src/KovaaksCompanion.App`: WPF shell. Views, popups, charts, tray, window.
- `scripts/`: build-time helpers (download and pack third-party binaries, convert data files).
- `docs/wiki/`: knowledge base. Start at `docs/wiki/INDEX.md`.

Dependency direction: `App` depends on `Core`. `Core` never depends on `App`.
Module map: [docs/wiki/structure.md](docs/wiki/structure.md).

## Build

```
dotnet build KovaaksCompanion.slnx -c Release
```

The build downloads a pinned, hash-checked OBS Studio release. A change is ready when this build succeeds without new warnings.

## Rules

- Code, comments, identifiers, commit messages: English.
- Follow the style of the surrounding code (naming, comment density, idiom). No drive-by reformatting.
- Keep Core free of WPF and Win32 UI types. New UI goes in `App`.
- User data is irreplaceable. The `archive` folder under the data folder (SQLite `archive.db` and `.rep` files) holds server scores that cannot be fetched again. Any change touching its schema or file layout needs a migration that preserves existing data.
- Cache files are disposable and may be rebuilt. Do not store anything irreplaceable there.
- Do not copy code from sources whose license is unclear or incompatible with GPL-2.0-or-later. Write your own implementation and credit data sources in `THIRD-PARTY-NOTICES.md`.
- Do not add telemetry, hooks into the game, or anything that modifies game files.
- Do not commit secrets, local paths, personal data, or build output.

## Contributing

Fork the repository, make a branch, open a pull request. See [CONTRIBUTING.md](CONTRIBUTING.md).
