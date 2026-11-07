# Contributing

Issues and pull requests are welcome in Japanese or English.

Use Windows and .NET 10 SDK. Keep file movement explicit and reversible. Do not introduce automatic file deletion, overwriting, or telemetry. Use `--demo` or `--desktop-dir` for manual tests, never a real desktop with important files.

Before submitting:

```powershell
dotnet build src/Lateraroot/Lateraroot.csproj -c Release
dotnet run --project tests/Lateraroot.Core.Tests -c Release
```

For UI changes, check hover open/close, keyboard access, left and right edges, empty stacks, search, and settings. For storage changes, verify original contents, collisions, journal recovery, and partial failures.

Describe the behavior changed and the checks performed. Avoid uploading personal desktop screenshots, settings, manifests, or file paths in issues.
