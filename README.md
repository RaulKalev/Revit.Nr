# Renumber — Revit Sheet Renumbering Tool

A Revit add-in for bulk sheet management: duplicate sheets, batch rename with find/replace, apply prefixes/suffixes, and preview all changes before committing.

## Features

- **Batch Renumber** — Rename multiple sheets at once using find/replace, prefix, or suffix rules
- **Duplicate Sheets** — Duplicate sheets in bulk with configurable naming
- **Preview Changes** — Review every rename before applying
- **Highlighting** — Visual highlight of affected elements in the Revit view
- **Persistent Settings** — Settings are saved per-project session

## Compatibility

| Revit Version | .NET Target |
|---------------|-------------|
| Revit 2024    | net48        |
| Revit 2026    | net8.0-windows |

## Installation

1. Build the solution (`Release` configuration).
2. Copy the output DLL and `.addin` manifest to your Revit add-ins folder:
   - **Revit 2024:** `%APPDATA%\Autodesk\Revit\Addins\2024\`
   - **Revit 2026:** `%APPDATA%\Autodesk\Revit\Addins\2026\`
3. Launch Revit — the **RK Tools** tab will appear in the ribbon.

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
UI/                     # WPF views, view models, and converters
Assets/                 # Embedded resources (icons)
Properties/             # Assembly info and settings
```

## Dependencies

- [ricaun.Revit.UI](https://github.com/ricaun-io/ricaun.Revit.UI) — Ribbon helpers
- [MaterialDesignThemes](https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit) — UI theming
- [Costura.Fody](https://github.com/Fody/Costura) — Assembly merging
- [Newtonsoft.Json](https://www.newtonsoft.com/json) — Settings serialization
- [netDxf](https://github.com/haplokuon/netDxf) — DXF support

## License

MIT
