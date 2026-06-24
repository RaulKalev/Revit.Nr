# Renumber — Revit Circuit & Parameter Sequencing Tool

A Revit add-in for sequentially numbering electrical circuits and element parameters by clicking elements one at a time. Supports EL (electrical panels), LPS (lightning protection), Üld (generic), Heli (alternating), ATS (padded numbering), and Side (single/double pair) modes.

## Features

### EL Mode — Electrical Circuits
- Pick electrical panel circuits one by one to assign sequential parameter values
- Supports custom parameter name and starting value
- **↑ / ↓ direction** — ascend (1, 2, 3…) or descend (3, 2, 1… into negatives)

### LPS Mode — Lightning Protection
- Sequential numbering across lightning protection circuits
- Inner-range (IR) cycling through a sub-sequence alongside the main value
- **↑ / ↓ direction** — bidirectional for both main value and inner-range index

### Üld Mode — Generic Category
- Pick any Revit element from a configurable category (Walls, Doors, Rooms, MEP, etc.)
- Set parameter name, starting value, prefix and suffix
- **↑ / ↓ direction** — ascend or descend with prefix/suffix preserved

### Heli Mode — Alternating Values
- Alternates between two fixed values on successive picks (e.g. A/B, A/B…)
- Category and parameter name configurable

### ATS Mode — Padded Numbering
- Left-pads the numeric value to a fixed character width using a fill character (e.g. `001`, `002`)
- Configurable Char Count, Prefix (fill char), and Suffix
- Optional second parameter written with a fixed value on every pick
- **↑ / ↓ / = direction** — ascend, descend, or freeze

### Side Mode — Single / Double Pair Numbering
- Category, parameter name, and incrementing value like Üld
- **Double** — tick to write a pair of values separated by a configurable divider (e.g. `01/02 → 03/04`); both values increment by 2 per pick
- **Circuit Limit** — value wraps back to 1 after reaching the limit (default 24); Reset button sets values back to 1 (and 2 in Double mode)
- **Char Count + Prefix (fill char) + Suffix** — same left-padding as ATS applied to each value independently
- **Runtime Double toggle in the status popup** — uncheck during a session to write single values for individual elements, then re-check to resume pairs; both counters stay in sync throughout

### General
- **Status overlay** — floating window shows current value and mode while picking; ▲/▼ nudge buttons adjust the value mid-session
- **Batch Setup** — configure multiple panels and lines in one session
- **Grouping view** — inspect and manage circuit groupings
- **Highlighting** — visual view-filter highlight of selected elements
- **Persistent settings** — all window state, field values, and direction preferences saved to `config.json`

## Compatibility

| Revit Version | .NET Target     |
|---------------|-----------------|
| Revit 2024    | net48           |
| Revit 2026    | net8.0-windows  |

## Installation

1. Build the solution (`Release` configuration).
2. Copy the output DLL and `.addin` manifest to your Revit add-ins folder:
   - **Revit 2024:** `%APPDATA%\Autodesk\Revit\Addins\2024\`
   - **Revit 2026:** `%APPDATA%\Autodesk\Revit\Addins\2026\`
3. Launch Revit — the **RK Tools** tab will appear in the ribbon.

Settings are stored at `C:\ProgramData\RK Tools\Renumber\config.json`.

## Building

Requirements:
- Visual Studio 2022+ or the .NET 8 SDK
- Autodesk Revit SDK (resolved automatically via NuGet)

```bash
dotnet build Renumber.sln
```

## Project Structure

```
App.cs                  # IExternalApplication entry point
Commands/               # Revit IExternalCommand implementations
Models/                 # DTOs and result types
Services/               # Business logic, settings, parameter resolution
  Core/                 # Logging infrastructure
  Revit/                # External event requests (EL, LPS, Üld, Heli, ATS, Side, etc.)
UI/                     # WPF views, view models, converters, and themes
  ViewModels/           # MVVM view models
  Controls/             # Custom controls (CircularGauge)
  Themes/               # Dark/light theme resources
Assets/                 # Embedded resources (icons)
Properties/             # Assembly info
```

## Dependencies

- [ricaun.Revit.UI](https://github.com/ricaun-io/ricaun.Revit.UI) — Ribbon helpers
- [MaterialDesignThemes](https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit) — UI theming
- [Costura.Fody](https://github.com/Fody/Costura) — Assembly merging
- [Newtonsoft.Json](https://www.newtonsoft.com/json) — Settings serialization
- [netDxf](https://github.com/haplokuon/netDxf) — DXF support

## License

MIT
