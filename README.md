# Kiitou

Portable Windows hosts and address manager.

## Development status

**Pass 1: Foundation only.** Hosts-file modification, connectivity testing,
profile editing, the approved final UI, and release packaging are not implemented.

## Technology

- C# and .NET 10
- Windows Presentation Foundation (WPF)
- Local JSON configuration
- Portable-first writable storage, with local AppData fallback

## Build (Windows)

Install the .NET 10 SDK, then run:

```powershell
dotnet build .\Kiitou\Kiitou.csproj -c Release
dotnet run --project .\Kiitou\Kiitou.csproj
```

The bootstrap window is intentionally minimal. It is not the approved final UI.

## Storage

Kiitou creates `config.json` beside the executable when the folder is writable.
When that directory cannot be written, Kiitou uses
`%LOCALAPPDATA%\Kiitou\config.json`.

The default configuration contains a disabled-by-default empty mapping collection,
a Default profile, and the purple/dark theme. No hosts-file changes occur in Pass 1.

## Theme foundations

Seven approved color families: purple, blue, green, gold, red, silver, teal.
Each supports light and dark modes. Matching icon filenames are reserved in
`ThemeCatalog`; actual icon embedding and runtime switching belong to Pass 7.

## Scope

No features outside the user-approved development checklist are authorized.
See `docs/PASS_01_AUDIT.md` for validation limitations.
