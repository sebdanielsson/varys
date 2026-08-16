# app/

- **`Varys/`** — the WinUI 3 desktop app (C# / .NET 10). It owns the UI and supervises the Python
  sidecar, talking to it over a localhost WebSocket (live captions) and REST (control + library).
- **`branding/`** — `logo.svg` plus `gen_assets.py`, which generates the app icon and tile assets.

Build and run it with `dotnet run -c Debug -p:Platform=x64` from `Varys/`; see
[../CONTRIBUTING.md](../CONTRIBUTING.md) for the full development setup and
[../docs/architecture.md](../docs/architecture.md) for the IPC contract.
