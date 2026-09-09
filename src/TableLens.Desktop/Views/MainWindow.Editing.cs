using System.Data;
using Avalonia.Controls;
using Avalonia.Interactivity;
using DotNetDBF;
using TableLens.Core;
using TableLens.Core.Dbf;
using TableLens.Core.Editing;
using TableLens.Core.Models;

namespace TableLens.Desktop.Views;

public sealed partial class MainWindow
{
    private bool CommitGrid() => DataGrid.CommitEdit();
    private async Task<bool> ConfirmTableChangesAsync()
    {
        if (!CommitGrid()) return false;
        if (_document is null || (!_history.HasChanges && !_document.IsNew)) return true;
        var choice = await Dialogs.Choice(this, Lang.T("Unsaved"), Lang.T("UnsavedText"), "Save", "Discard", "Cancel");
        if (choice == "Save") return await SaveDocumentAsync();
        if (choice != "Discard") return false;
        _history.UndoAll(); _history.Clear();
        // A new document still has no file on disk; callers replace or close it.
        return true;
    }

    private async Task<bool> SaveDocumentAsync()
    {
        if (_document is null || !CommitGrid()) return false;
        _writing = true; SetBusy(true, "Working"); UpdateEditingState();
        try
        {
            var document = _document;
            var result = await Task.Run(() => _files.Save(document));
            document.Table.AcceptChanges(); _history.Clear();
            StatusText.Text = Lang.T("Saved") + (result.BackupPath is null ? "" : " · " + Lang.T("Backup") + ": " + System.IO.Path.GetFileName(result.BackupPath));
            _currentEntry?.SetStatus($"{document.Table.Rows.Count:N0} {Lang.T("Rows").ToLowerInvariant()}");
            return true;
        }
        catch (Exception ex) { await Dialogs.Error(this, ex); return false; }
        finally { _writing = false; SetBusy(false); UpdateEditingState(); }
    }

    private async void SaveClick(object? sender, RoutedEventArgs e) => await Run(async () => { if (_document is not null && CommitGrid() && (_history.HasChanges || _document.IsNew)) await SaveDocumentAsync(); });
    private async void EditModeClick(object? sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (_document is null) return;
        if (!_document.CanEdit) { await Dialogs.Message(this, Lang.T("ReadOnly"), _document.ReadOnlyReason!); return; }
        if (_editMode && !await ConfirmTableChangesAsync()) return;
        _editMode = !_editMode; UpdateEditingState();
    });
    private void UndoClick(object? sender, RoutedEventArgs e) { if (_writing || !CommitGrid()) return; _history.Undo(); RefreshGridRows(); RefreshSchema(); UpdateSummary(); }
    private void RedoClick(object? sender, RoutedEventArgs e) { if (_writing || !CommitGrid()) return; _history.Redo(); RefreshGridRows(); RefreshSchema(); UpdateSummary(); }
    private async void HistoryClick(object? sender, RoutedEventArgs e) => await Run(() => Dialogs.ShowTable(this, Lang.T("History"), _history.Entries.Select(h => new { Time = h.Timestamp.ToString("HH:mm:ss"), h.Description, Status = Lang.T(h.IsApplied ? "Applied" : "Undone") }).ToArray(), Lang.T("HistoryHelp")));

    private async void AddRowClick(object? sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (!_editMode || _document is null || !CommitGrid()) return;
        if (_document.Format == TableFormat.Json && _document.JsonLayout == JsonLayout.SingleObject && _document.Table.Rows.Count >= 1)
            throw new InvalidOperationException("До одиночного JSON-об'єкта не можна додати другий рядок. Використовуйте масив об'єктів.");
        var table = _document.Table;
        var row = table.NewRow();
        foreach (DataColumn column in table.Columns) if (column.DataType == typeof(string)) row[column] = "";
        var values = await Dialogs.EditRow(this, row, Lang.T("AddRow"));
        if (values is null) return;
        foreach (var pair in values) row[pair.Key] = pair.Value;
        _history.Execute(new AddRowEditAction(table, row, table.Rows.Count));
        RefreshGridRows(); UpdateSummary();
    });
    private async void EditRowClick(object? sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (!_editMode || !CommitGrid()) return;
        if (DataGrid.SelectedItem is not GridRow rowView) { await Dialogs.Message(this, Lang.T("EditRow"), Lang.T("SelectRow")); return; }
        var row = rowView.Row;
        var values = await Dialogs.EditRow(this, row, Lang.T("EditRow"));
        if (values is null) return;
        var changed = values.Where(p => !Equals(row[p.Key], p.Value)).ToArray();
        if (changed.Length == 0) return;
        var before = changed.ToDictionary(p => p.Key, p => row[p.Key]);
        _history.Execute(new DelegateEditAction(Lang.T("EditRow"), () => { foreach (var p in before) row[p.Key] = p.Value; }, () => { foreach (var p in changed) row[p.Key] = p.Value; }));
        RefreshGridRows(); UpdateSummary();
    });
    private async void DeleteRowsClick(object? sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (!_editMode || _document is null || !CommitGrid()) return;
        var selected = DataGrid.SelectedItems.OfType<GridRow>().Select(r => r.Row).ToArray();
        if (selected.Length == 0) return;
        if (_document.Format == TableFormat.Json && _document.JsonLayout == JsonLayout.SingleObject) throw new InvalidOperationException("Одиночний JSON-об'єкт має містити один рядок.");
        if (await Dialogs.Choice(this, Lang.T("DeleteRows"), Lang.T("DeleteConfirm"), "Delete", "Cancel") != "Delete") return;
        _history.Execute(new DeleteRowsEditAction(_document.Table, selected));
        RefreshGridRows(); UpdateSummary();
    });

    private async void SchemaEditClick(object? sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (!_editMode || _document?.Dbf is not { } data || !CommitGrid()) return;
        var definitions = await Dialogs.Schema(this, data.Fields.Select((f, i) => new DbfFieldDefinition(f.Name, data.SourceFields[i].DataType, f.Length, f.DecimalCount, f.Name)));
        if (definitions is null) return;
        var document = _document;
        var before = new DbfSchemaSnapshot(data.Fields, data.SourceFields, data.Table);
        var after = new DbfSchemaService().Transform(data, definitions, document.Encoding);
        _history.Execute(new DelegateEditAction(Lang.T("SchemaEdit"), () => ApplySchema(document, before), () => ApplySchema(document, after)));
    });
    private void ApplySchema(TableDocument document, DbfSchemaSnapshot snapshot)
    {
        CaptureColumnLayout();
        document.Table = snapshot.Table; document.Dbf!.Table = snapshot.Table; document.Dbf.Fields = snapshot.Fields; document.Dbf.SourceFields = snapshot.SourceFields;
        BindDocument(document, clearHistory: false); _editMode = true; UpdateEditingState();
    }

    private async void NewTableClick(object? sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (!await ConfirmTableChangesAsync()) return;
        var choice = await Dialogs.NewTable(this);
        if (choice is null) return;
        IReadOnlyList<DbfFieldDefinition>? fields = null;
        if (choice.Format == TableFormat.Dbf)
        {
            fields = await Dialogs.Schema(this, choice.Columns.Select(n => new DbfFieldDefinition(n, NativeDbType.Char, 50, 0, null)));
            if (fields is null) return;
        }
        var extension = choice.Format.ToString().ToLowerInvariant();
        var path = await PickSaveAsync(Lang.T("New"), "untitled." + extension, extension);
        if (path is null) return;
        if (File.Exists(path)) throw new IOException("Для нової таблиці оберіть ім'я, яке ще не використовується.");
        TableDocument document;
        if (fields is not null) document = DbfDocuments.Create(path, fields, _settings.Import.DbfCodePage);
        else
        {
            var table = new DataTable("untitled");
            foreach (var name in choice.Columns) table.Columns.Add(name, typeof(string));
            document = new() { FilePath = path, Format = choice.Format, Table = table, Encoding = TableFileService.StrictEncoding(65001), Options = new ReadOptions { Delimiter = "," }, JsonLayout = JsonLayout.ObjectArray, IsNew = true };
        }
        CaptureColumnLayout(); BindDocument(document); _editMode = true;
        var entry = new FileEntry(path, document.Options.Clone()); _entries.Add(entry); _currentEntry = entry; SetSelection(entry);
        _projectDirty |= _project is not null; RefreshFileList(); UpdateProjectLabel(); UpdateEditingState();
    });

    private async void ExportClick(object? sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (_document is null || !CommitGrid()) return;
        var choice = await Dialogs.Export(this, _document);
        if (choice is null) return;
        var extension = choice.Format.ToString().ToLowerInvariant();
        var path = await PickSaveAsync(Lang.T("Export"), System.IO.Path.GetFileNameWithoutExtension(_document.FilePath) + "-export." + extension, extension);
        if (path is null) return;
        var document = _document;
        var subset = choice.VisibleOnly && _view is not null ? _view.Cast<DataRowView>().Select(r => r.Row).ToArray() : null;
        _writing = true; SetBusy(true, "Working");
        try { var result = await Task.Run(() => _files.ExportRows(document, path, choice.Format, subset)); StatusText.Text = Lang.T("Exported") + ": " + result.FilePath; }
        finally { _writing = false; SetBusy(false); }
    });
}
