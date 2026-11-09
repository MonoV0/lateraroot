# Contributing

Issues and pull requests are welcome in Japanese or English.

Use Windows and .NET 10 SDK. Keep file movement explicit and reversible. Do not introduce automatic file deletion, overwriting, or telemetry. Use `--demo` or `--desktop-dir` for manual tests, never a real desktop with important files.

Before submitting:

```powershell
./scripts/Prepare-Icons.ps1 # Windows, PowerShell 7; pinned official sources, SHA256-verified
dotnet build src/Lateraroot/Lateraroot.csproj -c Release
dotnet run --project tests/Lateraroot.Core.Tests -c Release
```

For UI changes, check desktop/compact transitions, hover open/close, keyboard access, all four edges, dragging, minimize/hide/restore, empty genres, search, and settings. Run the isolated UI verifier documented in README. For storage changes, verify original contents, collisions, journal recovery, and partial failures. Keep the bundled font and its OFL license together.

Describe the behavior changed and the checks performed. Avoid uploading personal desktop screenshots, settings, manifests, or file paths in issues.

UI artwork is **Uicons by Flaticon**, under its separate license, not MIT. Keep the visible Settings credit and third-party notices. Do not commit the downloaded font or generated geometry, or redistribute either as an icon pack. The preparation script pins the official source revision and verifies its hashes; runtime uses only 21 compiled vectors.

For updater changes, run the real portable-package upgrade and corrupt-package rejection checks in `.github/workflows/build.yml`. Keep the Velopack package ID `LaterarootApp` separate from `%LOCALAPPDATA%\Lateraroot\Data`. Do not add silent installation, telemetry or updates during a file operation. Never publish test-only future versions or feeds.

Run `./scripts/Prepare-Shader.ps1` before building renderer changes. It compiles the MIT-licensed HLSL with Windows' built-in d3dcompiler_47; no compiler is distributed. Build an isolated read-only preview with `dotnet publish src/Lateraroot/Lateraroot.csproj -c Release -r win-x64 --self-contained true -p:PreviewBuild=true`. Verify its guard with `--demo --verify-preview --data-dir <fresh-test-directory>`. On a physical GPU use `--demo --verify-glass` with a fresh test directory; this captures only the owned synthetic test scene. Never use personal desktop captures as repository proof.
