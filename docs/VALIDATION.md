# Validation

Validated on 2026-10-03 in a Linux x64 environment using .NET SDK 10.0.302/10.0.401 and Avalonia 12.1.2.

## Completed checks

- Release compilation of the core, desktop app and test project.
- Automated round-trip checks for CSV/TSV quoting, multiline fields, Unicode, leading-zero identifiers, BOM encodings and headerless input.
- JSON metadata, object/array layouts, types, nested values, mixed values, missing properties and filtered exports.
- DBF text, numbers, dates, logical values, stale record-count headers, corrupted numeric records, width validation and unrepresentable characters.
- Backups and rejection of externally modified source files, including a change with the same length and timestamp.
- Null/empty/OR filters, escaped field names, numeric CSV comparisons and cancelled reads.
- Undo/redo of added/deleted rows, schema transformations and relative project paths, including importing the old WinForms file-list format.
- Actual Avalonia window/control tests for data display, search, schema tabs, inline edit, Undo and cancelling an edit.
- Real Skia-rendered screenshots of the welcome screen and light/dark themes, visually inspected for layout.
- Bash publishing-script syntax and macOS bundle plist parsing.
- Successful Linux x64 self-contained publication, including the executable, runtime, examples and dependency license.
- GitHub workflow/Dependabot YAML syntax and local documentation links.

The test suite contains **36 test cases**. The screenshot test captures images only when `TABLELENS_SCREENSHOT_DIR` is set; without it the test returns without writing artifacts.

## Native platform verification

The desktop shell was exercised through Avalonia Headless with real rendering, not by launching native Windows/macOS desktop sessions. Native file pickers, platform drag-and-drop, permissions, DPI scaling and macOS Gatekeeper should be checked on their respective OS hosts. The included GitHub Actions matrix will compile and run headless tests on all three OS families after the project is pushed; those hosted runs have not happened in this workspace.

The release workflow cross-publishes five runtime targets. Apple Silicon/Intel and Linux Arm64 artifacts should additionally be launched on their intended hardware. macOS Developer ID signing and notarization are intentionally left to the repository owner's release credentials.

The PowerShell packaging script was reviewed but not executed in this environment; the Bash publisher was executed successfully.

To reproduce the suite:

```bash
dotnet build TableLens.sln -c Release
dotnet test tests/TableLens.Tests/TableLens.Tests.csproj -c Release
```

To regenerate the documented UI images on Linux/macOS:

```bash
TABLELENS_SCREENSHOT_DIR="$PWD/docs" dotnet test tests/TableLens.Tests/TableLens.Tests.csproj -c Release --filter CaptureRealLightAndDarkScreenshotsWhenRequested
```
