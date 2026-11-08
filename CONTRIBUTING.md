# Contributing

Issues and pull requests are welcome in Japanese or English.

Use Windows and .NET 10 SDK. Keep file movement explicit and reversible. Do not introduce automatic file deletion, overwriting, or telemetry. Use `--demo` or `--desktop-dir` for manual tests, never a real desktop with important files.

Before submitting:

```powershell
dotnet build src/Lateraroot/Lateraroot.csproj -c Release
dotnet run --project tests/Lateraroot.Core.Tests -c Release
```

For UI changes, check desktop/compact transitions, hover open/close, keyboard access, all four edges, dragging, minimize/hide/restore, empty genres, search, and settings. Run the isolated UI verifier documented in README. For storage changes, verify original contents, collisions, journal recovery, and partial failures. Keep the bundled font and its OFL license together.

Describe the behavior changed and the checks performed. Avoid uploading personal desktop screenshots, settings, manifests, or file paths in issues.

For updater changes, run the real portable-package upgrade and corrupt-package rejection checks in `.github/workflows/build.yml`. Keep the Velopack package ID `LaterarootApp` separate from `%LOCALAPPDATA%\Lateraroot\Data`. Do not add silent installation, telemetry or updates during a file operation. Never publish test-only future versions or feeds.
