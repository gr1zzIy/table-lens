# Architecture

TableLens keeps the code split at the boundary that matters: file processing versus desktop controls. A test project verifies both. There are no extra application/domain/infrastructure projects and no runtime dependency injection.

| Project | Responsibility |
| --- | --- |
| `TableLens.Core` | DBF/CSV/JSON readers and writers, source fingerprints, safe replacement, project persistence, schema comparison and undo actions |
| `TableLens.Desktop` | Avalonia XAML, file dialogs, dynamic grid columns, editing dialogs and workspace coordination |
| `TableLens.Tests` | Format/data regression tests and tests of the actual Avalonia window and controls |

## Shared table model

`TableDocument` carries a `DataTable`, file format, import settings and format-specific metadata. CSV values remain strings to preserve identifiers, spacing and precision. JSON columns use decimal/bool/string when consistent and object for mixed or nested values. Nested JSON and numbers outside decimal range are retained as cloned `JsonElement` values. Missing JSON keys are tracked separately from explicit null.

DBF uses the existing `DbfTableData` and field descriptors. File reading never depends on a Windows UI type. Unsupported or partially read DBF documents stay read-only so unrecognized fields are not lost on save.

`TableFileService` dispatches by extension. It contains the two straightforward text-format implementations; DBF delegates to the reused reader/writer. Adding another format begins with a `TableFormat` value and one reader/writer branch. Extract a dedicated class if that format grows; no factory or plugin abstraction is necessary now.

## UI and bindings

`MainWindow` is split into focused partial files for files, workspace state, table presentation, editing and tools. Dialogs use native Avalonia controls and the storage-provider APIs. `GridRow` exposes one integer indexer to avoid ambiguity in DataRowView's overloaded indexers, including unusual JSON property names.

Filtering uses `DataView` and the inherited expression parser. Numeric-looking CSV text columns compare numerically without changing stored values. Input is debounced; an invalid expression keeps the prior view. Rendering uses DataGrid virtualization, but file data and visible row wrappers are held in memory.

The official MIT-licensed DataGrid is used for its editing behavior. Avalonia currently marks it deprecated and maintains it for bug fixes. This is a deliberate contained dependency; the commercial TreeDataGrid is not required. The core model and writers do not depend on either grid. The component's official status is documented [here](https://github.com/AvaloniaUI/Avalonia.Controls.DataGrid).

## Edits and undo

The existing small action-based undo manager handles cell edits, row addition/deletion and schema changes. Schema snapshots copy a table only when the structure changes. Ordinary edits do not copy the whole document. Removed DataRows lose their backing record buffers, so row actions preserve their values explicitly before detaching them.

Saving clears the active session history. A new edit after undo discards the redo branch. Table and project changes have separate save/discard prompts because saving a project does not write table data.

## Safe writes

1. Reject writes to read-only formats or a source whose SHA-256 changed since reading.
2. Write to a uniquely named temporary file in the destination directory.
3. Validate DBF field widths/precision and read the generated DBF back; validate JSON output with the parser.
4. Recheck the source fingerprint immediately before replacement.
5. Replace the file and keep a dated backup, or move a new file into place.
6. Update the document fingerprint only after success.

This is protection against common editing mistakes and external modifications. It is not a distributed locking protocol: another process can still race the final fingerprint check and atomic replacement. Keep a file closed in other writers while saving. CSV exports deliberately turn typed values into text; they cannot preserve JSON nesting as typed fields.

## Platform details

Paths use the OS comparison rules rather than treating all platforms as case-insensitive. Folder discovery is recursive, skips inaccessible directories and does not follow reparse-point loops. Projects store relative paths; settings/catalog use `LocalApplicationData` rather than the executable directory. Native file dialogs are accessed through Avalonia StorageProvider. Mac keyboard shortcuts use the command modifier.

`Lang` holds Ukrainian/English string pairs. The initial language is chosen before XAML loads; changing it takes effect after restart. Theme changes apply immediately. There is no localization package dependency.
