using System.Data;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using TableLens.Core;
using TableLens.Core.Editing;
using TableLens.Core.Filtering;
using TableLens.Core.Models;

namespace TableLens.Desktop.Views;

public sealed partial class MainWindow
{
    private readonly Dictionary<string, TextBox> _filterInputs = [];
    private string? _sortColumn;
    private bool _sortDescending;
    private (GridRow Row, int Index, object Value)? _editingCell;

    private void GridPreparingCell(object? sender, DataGridPreparingCellForEditEventArgs e)
    {
        if (e.Row.DataContext is GridRow row && e.Column.Tag is string name)
        {
            var index = row.Row.Table.Columns[name]!.Ordinal;
            _editingCell = (row, index, row.Row[index]);
        }
    }
    private void GridCellEditEnded(object? sender, DataGridCellEditEndedEventArgs e)
    {
        // Cell cancellation doesn't necessarily cancel the row transaction.
        // Restore this cell explicitly so Escape never commits typed text later.
        if (_editingCell is { } cell)
        {
            if (e.EditAction == DataGridEditAction.Cancel) cell.Row[cell.Index] = cell.Value;
            else
            {
                var row = cell.Row.Row;
                var value = row[cell.Index];
                if (!Equals(cell.Value, value)) _history.RecordApplied(new CellValueEditAction(row, row.Table.Columns[cell.Index], cell.Value, value, row.Table.Rows.IndexOf(row) + 1));
            }
        }
        _editingCell = null;
        UpdateEditingState();
    }

    private void BindDocument(TableDocument document, bool clearHistory = true)
    {
        _document = document;
        _view = new DataView(document.Table);
        _columnFilters.Clear(); _filterInputs.Clear(); _sortColumn = null; _sortDescending = false;
        SearchBox.Text = "";
        _filterTimer.Stop();
        DataGrid.ItemsSource = null; DataGrid.Columns.Clear();
        DataGrid.CanUserSortColumns = false;
        foreach (DataColumn column in document.Table.Columns)
        {
            var gridColumn = new DataGridTextColumn { Header = BuildHeader(column), Binding = new Binding($"[{column.Ordinal}]") { Mode = BindingMode.TwoWay, Converter = new CellConverter(column) },
                IsReadOnly = column.DataType == typeof(object), Width = new DataGridLength(Math.Clamp(column.Caption.Length * 9 + 60, 150, 270)) };
            gridColumn.Tag = column.ColumnName;
            DataGrid.Columns.Add(gridColumn);
        }
        RestoreColumnLayout();
        RefreshGridRows();
        SchemaGrid.Columns.Clear();
        foreach (var (name, key, width) in new[] { ("Ordinal", "#", 50), ("Name", "Column", 180), ("Type", "Type", 100), ("Length", "Length", 85), ("Decimals", "Decimals", 100), ("Status", "Status", 155), ("Description", "Description", 380) })
            SchemaGrid.Columns.Add(new DataGridTextColumn { Header = Lang.T(key), Binding = new Binding(name), Width = new(width) });
        RefreshSchema();
        NoticePanel.IsVisible = document.ReadOnlyReason is not null || document.Warnings.Count > 0;
        NoticeText.Text = document.ReadOnlyReason ?? string.Join("\n", document.Warnings.Take(3));
        InfoText.Text = $"{Lang.T("Path")}: {document.FilePath}\n{Lang.T("Format")}: {document.FormatLabel}\n{Lang.T("Encoding")}: {document.Encoding.WebName}\n{Lang.T("Rows")}: {document.Table.Rows.Count:N0}\n{Lang.T("FieldsCount")}: {document.Table.Columns.Count}\n" +
            (document.IsNew ? "" : $"{Lang.T("Size")}: {new FileInfo(document.FilePath).Length:N0} bytes\n{Lang.T("Modified")}: {File.GetLastWriteTime(document.FilePath):g}");
        DiagnosticsText.Text = string.Join("\n", document.Warnings) + (document.Dbf is null ? "" : $"\nDBF signature: 0x{document.Dbf.Signature:X2}\nHeader records: {document.Dbf.DeclaredRecordCount:N0}\nLoaded records: {document.Table.Rows.Count:N0}\n" + (_catalog?.Validate(document.FilePath, document.Dbf.Fields)?.ToDetailedText() ?? ""));
        DocumentPath.Text = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(document.FilePath)) + " / " + System.IO.Path.GetFileName(document.FilePath);
        ToolTip.SetTip(DocumentPath, document.FilePath);
        DocumentPanel.IsVisible = true; WelcomePanel.IsVisible = false;
        _editMode = false;
        if (clearHistory) _history.Clear();
        UpdateEditingState();
    }

    private Control BuildHeader(DataColumn column)
    {
        var label = new Button { Content = column.Caption, Classes = { "quiet" }, Padding = new(3, 0), Height = 27, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, FontWeight = FontWeight.SemiBold };
        var description = _document?.Dbf is null ? null : _catalog?.GetDescription(_document.FilePath, column.Caption);
        ToolTip.SetTip(label, string.Join("\n", new[] { column.Caption, description, column.DataType.Name }.Where(s => !string.IsNullOrEmpty(s))));
        label.Click += (_, _) =>
        {
            if (_view is null || !CommitGrid()) return;
            try
            {
                _sortDescending = _sortColumn == column.ColumnName && !_sortDescending;
                _sortColumn = column.ColumnName;
                _view.Sort = $"[{EscapeColumn(column.ColumnName)}] {(_sortDescending ? "DESC" : "ASC")}";
                RefreshGridRows();
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or EvaluateException) { StatusText.Text = ex.Message; }
        };
        var filter = new TextBox { PlaceholderText = "abc / == / >10", MinWidth = 70, Height = 28, MinHeight = 28, FontSize = 11, Padding = new(5, 3), Margin = new(0, 3, 0, 0) };
        ToolTip.SetTip(filter, Lang.T("FilterHint"));
        filter.TextChanged += (_, _) => { _columnFilters[column.ColumnName] = filter.Text ?? ""; _filterTimer.Stop(); _filterTimer.Start(); };
        _filterInputs[column.ColumnName] = filter;
        return new StackPanel { Margin = new(5, 4), HorizontalAlignment = HorizontalAlignment.Stretch, Children = { label, filter } };
    }

    private static string EscapeColumn(string name) => name.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("]", "\\]", StringComparison.Ordinal);
    private void SearchChanged(object? sender, TextChangedEventArgs e) { _filterTimer.Stop(); _filterTimer.Start(); }
    private void ApplyFilters()
    {
        if (_view is null || _document is null || !CommitGrid()) return;
        try
        {
            var expressions = _columnFilters.Where(f => !string.IsNullOrWhiteSpace(f.Value) && _document.Table.Columns.Contains(f.Key))
                .Select(f => ColumnFilterParser.Build(_document.Table.Columns[f.Key]!, f.Value)).ToList();
            if (!string.IsNullOrWhiteSpace(SearchBox.Text)) expressions.Add(TableFilterBuilder.Build(_document.Table, null, FilterOperator.Contains, SearchBox.Text.Trim()));
            _view.RowFilter = string.Join(" AND ", expressions.Select(f => "(" + f + ")"));
            StatusText.Text = Lang.T("Ready"); RefreshGridRows(); UpdateSummary();
        }
        catch (Exception ex) when (ex is FormatException or EvaluateException or SyntaxErrorException or InvalidCastException)
        {
            // Keep the last valid view and the user's input so it can be corrected.
            StatusText.Text = Lang.T("FilterInvalid") + ": " + ex.Message;
        }
    }
    private void ResetFiltersClick(object? sender, RoutedEventArgs e)
    {
        SearchBox.Text = ""; _columnFilters.Clear();
        foreach (var input in _filterInputs.Values) input.Text = "";
        _filterTimer.Stop(); ApplyFilters();
    }
    private void RefreshGridRows()
    {
        if (_view is null) return;
        var selected = (DataGrid.SelectedItem as GridRow)?.Row;
        var rows = _view.Cast<DataRowView>().Select(r => new GridRow(r.Row, UpdateEditingState)).ToArray();
        DataGrid.ItemsSource = rows;
        DataGrid.SelectedItem = selected is null ? null : rows.FirstOrDefault(r => ReferenceEquals(r.Row, selected));
    }

    private void RefreshSchema()
    {
        if (_document is null) return;
        var validation = _document.Dbf is null ? null : _catalog?.Validate(_document.FilePath, _document.Dbf.Fields);
        var schema = _document.GetColumns().Select(c => new SchemaRow(c.Ordinal, c.Name, c.Type, c.Length, c.Decimals,
            validation?.UnexpectedFields.Contains(c.Name) == true ? "Додаткове" : validation?.CaseMismatches.Any(s => s.StartsWith(c.Name + " →", StringComparison.Ordinal)) == true ? "Регістр" : "OK",
            _catalog?.GetDescription(_document.FilePath, c.Name, c.Ordinal) ?? c.Description)).ToList();
        if (validation is not null) schema.AddRange(validation.MissingFields.Select(n => new SchemaRow(null, n, "—", null, null, "Відсутнє", _catalog?.GetDescription(_document.FilePath, n) ?? "")));
        SchemaGrid.ItemsSource = schema;
    }

    private void CaptureColumnLayout()
    {
        if (_document is null || DataGrid.Columns.Count == 0) return;
        _settings.Columns[_document.FilePath] = DataGrid.Columns.Select(c => new ColumnLayout(c.Tag?.ToString() ?? "", Math.Max(80, c.ActualWidth), c.DisplayIndex)).ToList();
    }
    private void RestoreColumnLayout()
    {
        if (_document is null || !_settings.Columns.TryGetValue(_document.FilePath, out var layout)) return;
        var ordered = layout.OrderBy(c => c.Order).ToArray();
        foreach (var saved in ordered)
        {
            var column = DataGrid.Columns.FirstOrDefault(c => c.Tag?.ToString() == saved.Name);
            if (column is null) continue;
            column.Width = new(Math.Clamp(double.IsFinite(saved.Width) ? saved.Width : 150, 80, 2000));
            column.DisplayIndex = Math.Clamp(saved.Order, 0, DataGrid.Columns.Count - 1);
        }
    }

    private async void CopyCellClick(object? sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (DataGrid.SelectedItem is GridRow row && DataGrid.CurrentColumn?.Tag is string column && Clipboard is not null)
            await Clipboard.SetTextAsync(TableDocument.Display(row.Row[column]));
    });
    private async void CopyRowsClick(object? sender, RoutedEventArgs e) => await Run(CopyRowsAsync);
    private async Task CopyRowsAsync()
    {
        if (_document is null || Clipboard is null) return;
        var selected = DataGrid.SelectedItems.OfType<GridRow>().Select(r => r.Row).ToHashSet();
        var columns = DataGrid.Columns.OrderBy(c => c.DisplayIndex).Select(c => c.Tag!.ToString()!).ToArray();
        var lines = new List<string> { string.Join('\t', columns.Select(c => _document.Table.Columns[c]!.Caption)) };
        lines.AddRange((_view?.Cast<DataRowView>().Select(v => v.Row) ?? []).Where(selected.Contains).Select(r => string.Join('\t', columns.Select(c => TableDocument.Display(r[c]).Replace("\t", " ").Replace("\r", " ").Replace("\n", " ")))));
        if (selected.Count > 0) await Clipboard.SetTextAsync(string.Join(Environment.NewLine, lines));
    }
}

internal sealed class CellConverter(DataColumn column) : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => TableDocument.Display(value);
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        try { return TableDocument.ParseCell(value?.ToString() ?? "", column); }
        catch (Exception ex) when (ex is FormatException or OverflowException) { return new BindingNotification(ex, BindingErrorType.DataValidationError); }
    }
}
internal sealed record SchemaRow(int? Ordinal, string Name, string Type, int? Length, int? Decimals, string Status, string Description);
