<p align="center">
  <img src="src/TableLens.Desktop/Assets/tablelens.svg" width="76" alt="TableLens logo">
</p>

# TableLens

**Desktop application for working with DBF, CSV and JSON files.**

TableLens is a local table viewer and editor for cases where opening a full database tool or spreadsheet application is unnecessary.

It can open several files at once, inspect table structure, search and filter records, edit data with Undo/Redo support, compare DBF schemas and export data to other formats.

All files are processed locally. TableLens does not require a database server, backend service or cloud account, and does not upload your data anywhere.

[Українська інструкція](docs/USER_GUIDE.uk.md) · [Architecture](docs/ARCHITECTURE.md) · [Validation](docs/VALIDATION.md)

![TableLens light theme](docs/tablelens-light.png)

## Features

- Open individual files or several files at once.
- Open a folder and discover supported files recursively in its subdirectories.
- Work with standalone files or save a collection of files as a `.tablelens.json` project.
- Search across all columns.
- Apply filters to individual columns and combine them.
- Sort records by clicking a column header.
- Inspect field names, types, DBF lengths, expected fields and descriptions in the **Schema** tab.
- Enable editing explicitly when changes are needed.
- Edit individual cells or an entire row.
- Add and delete records.
- Undo and redo changes.
- Detect when a source file has been modified by another application before saving.
- Save through a temporary file and keep a dated backup of the previous version.
- Export either all records or only the currently filtered view.
- Export data to UTF-8 CSV or structured JSON.
- Export DBF tables back to DBF.
- Create empty CSV, JSON and dBase III tables.
- Edit a DBF schema before saving changes to the source file.
- Compare the schemas of two DBF files.
- Compare DBF files in two folders recursively.
- Use Ukrainian or English.
- Follow the system theme or select light/dark mode manually.

![TableLens dark theme](docs/tablelens-dark.png)

## Supported formats

| Format | Reading | Editing and saving | Notes |
| --- | --- | --- | --- |
| DBF | DotNetDBF; companion DBT files are detected by the reader | dBase III without memo fields; Char, Numeric, Float, Date and Logical | CP866 is used by default, but another encoding can be selected. Damaged files, duplicate field names and unsupported DBF variants are opened read-only when possible. |
| CSV / TSV | Supports quoted delimiters, escaped quotes and multiline values | Yes, including adding and deleting rows | Automatically detects comma, semicolon, tab or pipe delimiters. Delimiter, encoding and header handling can also be selected manually. Values such as `000123` remain text. |
| JSON | Arrays of objects, a single object or an object containing an array | Yes | Numbers, booleans, null values, nested structures, missing properties and wrapper metadata are preserved. For ambiguous wrapper objects, the table array can be selected explicitly. |

### DBF

DBF editing is limited to formats that TableLens can save safely.

Editable DBF files currently use the dBase III format and support:

- Character
- Numeric
- Float
- Date
- Logical

Memo fields are not written.

TableLens can read DBF files with an associated DBT file, but unsupported or partially corrupted structures may be opened as read-only.

Direct conversion of arbitrary CSV or JSON files to DBF is not supported. A reliable DBF conversion requires explicit field types, lengths and conversion rules, so TableLens does not try to infer them automatically.

### CSV and TSV

CSV files do not contain a formal schema, so values are ultimately stored as text.

CSV also cannot reliably distinguish between an empty string and a null value.

When exporting to CSV, values are converted to their textual representation.

Duplicate or empty column names are normalized for display. Files with such headers are opened read-only because saving them back could change the structure unexpectedly. They can still be exported to a separate normalized CSV file.

### JSON

The most straightforward JSON format is an array of objects:

```json
[
  {
    "id": 1,
    "name": "First record"
  },
  {
    "id": 2,
    "name": "Second record"
  }
]
```

A single object is also supported.

TableLens can also work with wrapper objects such as:

```json
{
  "source": "example",
  "records": [
    {
      "id": 1,
      "name": "First record"
    }
  ]
}
```

If an object contains more than one possible array, you can select which property should be treated as the table.

Scalar arrays such as:

```json
[1, 2, 3]
```

are not considered tabular input.

Nested objects and arrays are displayed as JSON inside a cell and can be changed through **Edit row**.

An empty JSON array does not contain enough information to determine a column schema.

## Running from source

TableLens requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

The SDK baseline is defined in `global.json`, with compatible later feature bands allowed.

From the repository root:

```bash
dotnet restore TableLens.sln
dotnet run --project src/TableLens.Desktop
```

The solution can also be opened in Rider or a current version of Visual Studio with .NET 10 support.

No database, web server, LibreOffice or additional file conversion software is required.

### Opening files from the command line

Files can be passed directly to the application:

```bash
dotnet run --project src/TableLens.Desktop -- ./samples/customers.csv ./samples/projects.json
```

The **Try sample files** command opens the examples included in the repository.

`samples/no-header.tsv` is intended to be opened with **First CSV row contains headers** disabled.

## Supported platforms

The desktop application targets:

- Windows x64
- macOS Apple Silicon
- macOS Intel
- Linux x64
- Linux Arm64

Use an operating system supported by both [.NET 10](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md) and [Avalonia](https://docs.avaloniaui.net/docs/supported-platforms).

Typical supported environments include:

- Windows 11
- macOS 15 or newer
- Ubuntu 22.04 / 24.04

On Linux, a graphical desktop session and the usual X11/font dependencies are required.

File loading runs in the background so the UI remains responsive. Tables themselves are loaded into memory, so available RAM is the main practical limitation for very large files.

## Build and tests

Build the solution:

```bash
dotnet build TableLens.sln -c Release
```

Run the test suite:

```bash
dotnet test tests/TableLens.Tests/TableLens.Tests.csproj -c Release
```

Tests cover the main file operations and application behavior, including:

- DBF, CSV and JSON round trips
- backup creation
- detection of external file modifications
- corrupted DBF handling
- filtering
- Undo/Redo
- project portability
- Avalonia controls running in a headless environment

See [docs/VALIDATION.md](docs/VALIDATION.md) for the current validation status and checks that still require native platform hosts.

## CI

GitHub Actions builds and tests the project on Linux, Windows and macOS for pushes and pull requests.

Release workflows also run the test suite before packaging.

Cross-publishing an artifact for another architecture does not replace testing it on the actual target architecture.

## Packaging

PowerShell 7 can be used to create self-contained packages:

```powershell
./scripts/publish.ps1 -Runtime win-x64
./scripts/publish.ps1 -Runtime osx-arm64
./scripts/publish.ps1 -Runtime linux-x64
```

The script also generates a checksum for the resulting package.

Build output is written to:

```text
artifacts/
```

### Windows

Windows builds are packaged as portable ZIP archives.

They are self-contained, so the target machine does not need a separate .NET runtime installation.

### macOS

macOS packaging creates a `TableLens.app` bundle.

When packaging is performed on macOS, the script applies an ad-hoc signature.

Public distribution requires signing with an Apple Developer ID certificate and notarization. Signing credentials are not stored in the repository.

### Linux

Linux builds are packaged as `.tar.gz` archives so executable permissions are preserved.

An unpackaged self-contained directory can also be produced with:

```bash
bash scripts/publish.sh linux-x64
```

Release builds are not trimmed. Some parts of the application rely on dynamic Avalonia bindings and DBF-related reflection, which may be affected by trimming.

## Releases

Pushing a version tag such as `v1.0.0` starts the release workflow:

```bash
git tag v1.0.0
git push origin v1.0.0
```

The workflow runs tests and creates packages for the supported platforms.

It can also be started manually to produce downloadable artifacts without creating a GitHub Release.

## Filtering

Each column has its own filter field.

Some common examples:

| Filter | Meaning |
| --- | --- |
| `Kyiv` | Contains `Kyiv` |
| `==Kyiv` | Exactly `Kyiv` |
| `!Kyiv` | Does not contain `Kyiv` |
| `!=` | Not empty |
| `==null` | Empty or null |
| `=="null"` | Literal text `null` |
| `>10` | Greater than `10` |
| `<=100` | Less than or equal to `100` |
| `==Kyiv or ==Lviv` | Exactly `Kyiv` or `Lviv` |

Filters from different columns are combined with **AND**.

The global search field is applied together with column filters.

Numeric-looking values in CSV text columns are compared as numbers when possible. If a column contains non-numeric text, comparison operators use text ordering instead.

Date values can also be compared with operators such as `>`, `<`, `>=` and `<=`.

If a filter expression is invalid, TableLens keeps the last valid result instead of replacing the table with an incorrect view. The error is shown in the status bar.

## Keyboard shortcuts

On Windows and Linux, use **Ctrl**.

On macOS, use **⌘**.

| Action | Shortcut |
| --- | --- |
| Open files | `Ctrl/⌘ + O` |
| Add files | `Ctrl/⌘ + Shift + O` |
| New table | `Ctrl/⌘ + N` |
| Save | `Ctrl/⌘ + S` |
| Export | `Ctrl/⌘ + E` |
| Focus search | `Ctrl/⌘ + F` |
| Undo | `Ctrl/⌘ + Z` |
| Redo | `Ctrl/⌘ + Y` |
| Reload current table | `F5` |
| Copy selected rows | `Ctrl/⌘ + C` |

The grid context menu can also be used to copy a single cell.

When focus is inside a text input, standard text-editing shortcuts keep their usual behavior.

## Projects

TableLens can be used without creating a project.

You can simply open several files, work with them and close the application.

If the same collection of tables is used regularly, it can be saved as a `.tablelens.json` project.

A project stores information such as:

- project name
- paths to opened files
- file-specific import options

Relative file paths are used whenever possible, which makes project files easier to move together with their data.

The table contents themselves are not stored inside the project file.

Existing project JSON files from the previous WinForms version that contain `Name` and `FilePaths` can also be opened through **Open project**.

## Application data

User settings are stored in the operating system's application-data directory under:

```text
TableLens
```

This directory contains application-specific data such as:

- preferences
- DBF field catalog
- saved column widths
- saved column order

These files are kept separately from the application installation directory.

The DBF field catalog can be customized.

If you used a modified `dbf-field-catalog.json` with the previous WinForms application, it is not migrated automatically. Copy it manually into the TableLens application-data directory if you want to keep those customizations.

## Saving files safely

TableLens avoids writing directly over the source file in a single step.

When a file is saved:

1. the application checks whether the source has changed since it was opened;
2. the new content is written to a temporary file;
3. the current version is kept as a dated backup;
4. the temporary file replaces the source only after writing succeeds.

If another application changed the source file in the meantime, TableLens refuses to overwrite it automatically.

This reduces the risk of losing changes when the same file is open in several applications.

## Architecture

The solution contains three projects:

```text
TableLens.Core
TableLens.Desktop
TableLens.Tests
```

`TableLens.Core` contains file handling and table logic without depending on the UI.

`TableLens.Desktop` contains the Avalonia desktop application.

`TableLens.Tests` contains automated tests for core functionality and selected UI behavior.

The architecture is intentionally kept small.

There is no:

- dependency injection container
- service locator
- ORM
- repository layer
- plugin framework

The desktop UI uses XAML together with focused event handlers, while file-format logic remains outside the UI project.

This keeps the codebase straightforward to navigate and makes it easier to add new file operations or table-related features without introducing infrastructure that the application does not need.

More details are available in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Background

Part of the DBF functionality was carried over from the earlier WinForms version of the application, including:

- DBF field catalog support
- DBF reading
- filtering
- schema comparison
- editing logic

The current version moves that functionality into a cross-platform Avalonia application and separates file operations from the desktop UI.

## Privacy

TableLens works with local files.

It does not:

- upload table contents to a server
- require an account
- require an Internet connection for normal file operations
- run a background backend service

Project files contain file paths and import settings, not copies of the source data.

## License

TableLens is licensed under the [MIT License](LICENSE).

Third-party libraries remain subject to their respective licenses. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for details.
