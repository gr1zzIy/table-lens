using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using TableLens.Core;
using TableLens.Core.Dbf;
using TableLens.Core.Models;

namespace TableLens.Desktop.Views;

public sealed partial class MainWindow
{
    private async void CompareClick(object? sender, RoutedEventArgs e) => await Run(async () =>
    {
        var leftPath = (await PickFilesAsync(Lang.T("ComparePickLeft"), false)).FirstOrDefault();
        if (leftPath is null) return;
        var rightPath = (await PickFilesAsync(Lang.T("ComparePickRight"), false)).FirstOrDefault();
        if (rightPath is null) return;
        SetBusy(true, "Working");
        try
        {
            var result = await Task.Run(() =>
            {
                if (System.IO.Path.GetExtension(leftPath).Equals(".dbf", StringComparison.OrdinalIgnoreCase) && System.IO.Path.GetExtension(rightPath).Equals(".dbf", StringComparison.OrdinalIgnoreCase))
                {
                    var service = new DbfStructureComparer(); var encoding = TableFileService.StrictEncoding(_settings.Import.DbfCodePage);
                    return (object)service.Compare(service.ReadStructure(leftPath, encoding), service.ReadStructure(rightPath, encoding));
                }
                return SchemaComparison.Compare(_files.Read(leftPath, _settings.Import), _files.Read(rightPath, _settings.Import));
            });
            await Dialogs.ShowTable(this, Lang.T("Compare"), result, leftPath + "\n" + rightPath);
        }
        finally { SetBusy(false); }
    });
    private async void CompareFoldersClick(object? sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (_catalog is null) throw new InvalidOperationException(Lang.T("CatalogUnavailable"));
        var left = await PickFolderAsync(Lang.T("CompareFolderLeft")); if (left is null) return;
        var right = await PickFolderAsync(Lang.T("CompareFolderRight")); if (right is null) return;
        _loadCancellation = new(); SetBusy(true);
        try
        {
            var results = await Task.Run(() => SchemaComparison.CompareFolders(left, right, _catalog, _settings.Import.DbfCodePage, _loadCancellation.Token));
            await Dialogs.ShowTable(this, Lang.T("CompareFolders"), results, left + "\n" + right);
        }
        finally { _loadCancellation.Dispose(); _loadCancellation = null; SetBusy(false); }
    });
    private async void CatalogClick(object? sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (_catalog is null || _document?.Dbf is not { } data) return;
        var document = _document;
        var fields = new ObservableCollection<DescriptionDraft>(data.Fields.Select(f => new DescriptionDraft { Name = f.Name, Description = _catalog.GetDescription(document.FilePath, f.Name, f.Ordinal) ?? "" }));
        var grid = new DataGrid { ItemsSource = fields, AutoGenerateColumns = false, CanUserResizeColumns = true };
        grid.Columns.Add(new DataGridTextColumn { Header = Lang.T("Name"), Binding = new Binding("Name"), IsReadOnly = true, Width = new(150) });
        grid.Columns.Add(new DataGridTextColumn { Header = Lang.T("Description"), Binding = new Binding("Description"), Width = new(1, DataGridLengthUnitType.Star) });
        var update = new Button { Content = Lang.T("UpdateDescriptions") };
        var replace = new Button { Content = Lang.T("ReplaceReference") };
        var remove = new Button { Content = Lang.T("RemoveMissing") };
        var close = new Button { Content = Lang.T("Close") };
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal, Children = { update, replace, remove, close } };
        foreach (var button in buttons.Children) button.Margin = new(0, 0, 7, 7);
        var body = new DockPanel { LastChildFill = true };
        var help = new TextBlock { Text = Lang.T("CatalogHelp"), TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new(0, 0, 0, 12) };
        DockPanel.SetDock(help, Dock.Top); body.Children.Add(help); body.Children.Add(grid);
        var window = Dialogs.Shell(Lang.T("Catalog"), body, buttons, 920, 610);
        async Task Save(bool wholeSchema)
        {
            if (!grid.CommitEdit()) return;
            if (wholeSchema && await Dialogs.Choice(window, Lang.T("Catalog"), Lang.T("ReplaceConfirm"), "Confirm", "Cancel") != "Confirm") return;
            try
            {
                var edited = fields.Select(f => new DbfExpectedField(f.Name, f.Description)).ToArray();
                if (wholeSchema) _catalog.ReplaceSchema(document.FilePath, data.Fields, edited);
                else _catalog.UpsertFields(document.FilePath, data.Fields, data.Fields, edited);
                RefreshSchema(); StartSchemaScan(); StatusText.Text = Lang.T("Saved"); window.Close();
            }
            catch (Exception ex) { await Dialogs.Error(window, ex); }
        }
        update.Click += async (_, _) => await Save(false); replace.Click += async (_, _) => await Save(true);
        remove.Click += async (_, _) =>
        {
            var schema = _catalog.FindSchema(document.FilePath);
            var missing = _catalog.Validate(document.FilePath, data.Fields)?.MissingFields ?? [];
            if (schema is null || missing.Count == 0) return;
            if (await Dialogs.Choice(window, Lang.T("RemoveMissing"), string.Join(", ", missing), "Confirm", "Cancel") != "Confirm") return;
            try { _catalog.RemoveFields(document.FilePath, schema.Fields.Select((f, i) => (f, i)).Where(p => missing.Contains(p.f.Name)).Select(p => p.i).ToArray()); RefreshSchema(); StartSchemaScan(); window.Close(); }
            catch (Exception ex) { await Dialogs.Error(window, ex); }
        };
        close.Click += (_, _) => window.Close(); await window.ShowDialog(this);
    });
}

internal sealed class DescriptionDraft { public string Name { get; set; } = ""; public string Description { get; set; } = ""; }
