using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Data;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using DotNetDBF;
using TableLens.Core;
using TableLens.Core.Models;

namespace TableLens.Desktop.Views;

internal static class Dialogs
{
    public static async Task<string?> Choice(Window owner, string title, string message, params string[] choices)
    {
        var body = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 16), MaxWidth = 570 };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        var window = Shell(title, body, buttons, 640, 240);
        foreach (var choice in choices)
        {
            var button = new Button { Content = Lang.T(choice), IsDefault = choice == choices[0] };
            if (choice == "Save" || choice == "Confirm") button.Classes.Add("primary");
            button.Click += (_, _) => window.Close(choice);
            buttons.Children.Add(button);
        }
        return await window.ShowDialog<string?>(owner);
    }

    public static Task Message(Window owner, string title, string message) => Choice(owner, title, message, "Close");

    public static async Task About(Window owner)
    {
        const string developerUrl = "https://github.com/gr1zzIy";
        var developerLink = new Button { Content = "github.com/gr1zzIy", HorizontalAlignment = HorizontalAlignment.Left, Padding = new(0) };
        developerLink.Classes.Add("quiet");
        developerLink.Foreground = Brush.Parse("#4677F5");
        developerLink.Click += (_, _) => Process.Start(new ProcessStartInfo(developerUrl) { UseShellExecute = true });

        var body = new StackPanel { Spacing = 8, Children = {
            new TextBlock { Text = "TableLens 1.0", FontSize = 20, FontWeight = FontWeight.SemiBold },
            new TextBlock { Text = "DBF · CSV · TSV · JSON" },
            new TextBlock { Text = ".NET 10 / Avalonia UI" },
            new TextBlock { Text = "Windows · macOS · Linux" },
            new TextBlock { Text = Lang.T("Subtitle"), Margin = new(0, 8, 0, 8), Classes = { "muted" } },
            new TextBlock { Text = Lang.T("Developer"), FontWeight = FontWeight.SemiBold },
            developerLink
        } };
        var close = new Button { Content = Lang.T("Close"), IsDefault = true, IsCancel = true };
        var window = Shell(Lang.T("About"), body, close, 500, 330);
        close.Click += (_, _) => window.Close();
        await window.ShowDialog(owner);
    }

    public static Task Error(Window owner, Exception exception) => Message(owner, Lang.T("Error"), exception.Message);

    public static async Task<string?> Input(Window owner, string title, string initial = "")
    {
        var text = new TextBox { Text = initial, PlaceholderText = Lang.T("Name"), MinWidth = 370 };
        var buttons = Buttons(out var accept, out var cancel);
        var window = Shell(title, text, buttons, 470, 170);
        accept.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(text.Text)) window.Close(text.Text.Trim()); };
        cancel.Click += (_, _) => window.Close();
        window.Opened += (_, _) => { text.Focus(); text.SelectAll(); };
        return await window.ShowDialog<string?>(owner);
    }

    public static async Task<Dictionary<DataColumn, object>?> EditRow(Window owner, DataRow row, string title)
    {
        var form = new StackPanel { Spacing = 14 };
        form.Children.Add(new TextBlock { Text = Lang.T("RowHelp"), TextWrapping = TextWrapping.Wrap });
        var inputs = new List<(DataColumn Column, TextBox Text, CheckBox Null)>();
        foreach (DataColumn column in row.Table.Columns)
        {
            var label = new TextBlock { Text = column.Caption + "   ·   " + column.DataType.Name, FontWeight = FontWeight.SemiBold };
            var value = new TextBox { Text = TableDocument.Display(row[column]), AcceptsReturn = column.DataType == typeof(object), MinHeight = 34 };
            var nullBox = new CheckBox { Content = Lang.T("Null"), IsChecked = row.IsNull(column) };
            nullBox.IsCheckedChanged += (_, _) => value.IsEnabled = nullBox.IsChecked != true;
            value.IsEnabled = nullBox.IsChecked != true;
            form.Children.Add(new StackPanel { Spacing = 6, Children = { label, value, nullBox } });
            inputs.Add((column, value, nullBox));
        }
        var validation = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brush.Parse("#CF5961") };
        form.Children.Add(validation);
        var buttons = Buttons(out var accept, out var cancel);
        var window = Shell(title, new ScrollViewer { Content = form }, buttons, 670, 660);
        accept.Click += (_, _) =>
        {
            try { window.Close(inputs.ToDictionary(i => i.Column, i => TableDocument.ParseCell(i.Text.Text ?? "", i.Column, row[i.Column], i.Null.IsChecked == true))); }
            catch (Exception ex) when (ex is FormatException or System.Text.Json.JsonException) { validation.Text = ex.Message; }
        };
        cancel.Click += (_, _) => window.Close();
        return await window.ShowDialog<Dictionary<DataColumn, object>?>(owner);
    }

    public static async Task<ExportChoice?> Export(Window owner, TableDocument document)
    {
        var format = new ComboBox { ItemsSource = document.Dbf is null || !document.CanEdit ? new[] { "CSV", "JSON" } : new[] { "CSV", "JSON", "DBF" }, SelectedIndex = 0 };
        var visible = new CheckBox { Content = Lang.T("ExportVisible"), IsChecked = true };
        var body = new StackPanel { Spacing = 15, Children = { Label("Format"), format, visible, new TextBlock { Text = Lang.T("ExportHelp"), TextWrapping = TextWrapping.Wrap } } };
        var buttons = Buttons(out var accept, out var cancel);
        accept.Content = Lang.T("Export");
        var window = Shell(Lang.T("ExportTitle"), body, buttons, 520, 300);
        accept.Click += (_, _) => window.Close(new ExportChoice(format.SelectedItem?.ToString() switch { "DBF" => TableFormat.Dbf, "JSON" => TableFormat.Json, _ => TableFormat.Csv }, visible.IsChecked == true));
        cancel.Click += (_, _) => window.Close();
        return await window.ShowDialog<ExportChoice?>(owner);
    }

    public static async Task<AppSettings?> Settings(Window owner, AppSettings current, ReadOptions options)
    {
        var encodingNames = new[] { "UTF-8", "CP866", "Windows-1251", "Windows-1252", "UTF-16 LE" };
        var encodingIds = new[] { 65001, 866, 1251, 1252, 1200 };
        var dbfEncoding = new ComboBox { ItemsSource = encodingNames, SelectedIndex = Math.Max(0, Array.IndexOf(encodingIds, options.DbfCodePage)) };
        var csvEncoding = new ComboBox { ItemsSource = encodingNames, SelectedIndex = Math.Max(0, Array.IndexOf(encodingIds, options.TextCodePage)) };
        var delimiters = new[] { Lang.T("Auto"), ",", ";", "Tab", "|" };
        var separator = new ComboBox { ItemsSource = delimiters, SelectedIndex = options.Delimiter switch { "," => 1, ";" => 2, "\t" => 3, "|" => 4, _ => 0 } };
        var header = new CheckBox { Content = Lang.T("Header"), IsChecked = options.HasHeader };
        var property = new TextBox { Text = options.JsonArrayProperty, PlaceholderText = "data / records / items" };
        var themes = new[] { "System", "Light", "Dark" };
        var theme = new ComboBox { ItemsSource = themes.Select(Lang.T).ToArray(), SelectedIndex = Math.Max(0, Array.IndexOf(themes, current.Theme)) };
        var language = new ComboBox { ItemsSource = new[] { "Українська", "English" }, SelectedIndex = current.Language == "en" ? 1 : 0 };
        var body = new StackPanel { Spacing = 10, Children = {
            Label("Theme"), theme, Label("Language"), language, new TextBlock { Text = Lang.T("RestartLanguage"), Classes = { "muted" } },
            new Separator { Margin = new(0, 7) }, Label("Import"), new TextBlock { Text = Lang.T("ImportHelp"), TextWrapping = TextWrapping.Wrap, Classes = { "muted" } },
            new TextBlock { Text = "DBF · " + Lang.T("Encoding") }, dbfEncoding, new TextBlock { Text = "CSV / TSV · " + Lang.T("Encoding") }, csvEncoding,
            Label("Delimiter"), separator, header, Label("JsonProperty"), property } };
        var buttons = Buttons(out var accept, out var cancel);
        var window = Shell(Lang.T("Settings"), new ScrollViewer { Content = body }, buttons, 530, 700);
        accept.Click += (_, _) => window.Close(new AppSettings { Theme = themes[theme.SelectedIndex], Language = language.SelectedIndex == 1 ? "en" : "uk",
            Import = new ReadOptions { DbfCodePage = encodingIds[dbfEncoding.SelectedIndex], TextCodePage = encodingIds[csvEncoding.SelectedIndex], HasHeader = header.IsChecked == true,
                Delimiter = separator.SelectedIndex switch { 1 => ",", 2 => ";", 3 => "\t", 4 => "|", _ => null }, JsonArrayProperty = string.IsNullOrWhiteSpace(property.Text) ? null : property.Text.Trim() } });
        cancel.Click += (_, _) => window.Close();
        return await window.ShowDialog<AppSettings?>(owner);
    }

    public static async Task<NewTableChoice?> NewTable(Window owner)
    {
        var format = new ComboBox { ItemsSource = new[] { "CSV", "JSON", "DBF" }, SelectedIndex = 0 };
        var columns = new TextBox { Text = "ID, NAME", PlaceholderText = "ID, NAME, AMOUNT" };
        var validation = new TextBlock { Foreground = Brush.Parse("#CF5961"), TextWrapping = TextWrapping.Wrap };
        var body = new StackPanel { Spacing = 14, Children = { new TextBlock { Text = Lang.T("NewTableName"), TextWrapping = TextWrapping.Wrap }, Label("Format"), format, Label("Columns"), columns, validation } };
        var buttons = Buttons(out var accept, out var cancel);
        var window = Shell(Lang.T("New"), body, buttons, 540, 330);
        accept.Click += (_, _) =>
        {
            var names = (columns.Text ?? "").Split(',').Select(s => s.Trim()).ToArray();
            if (names.Any(string.IsNullOrWhiteSpace) || names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length) { validation.Text = "Укажіть різні непорожні назви полів."; return; }
            window.Close(new NewTableChoice(format.SelectedIndex switch { 2 => TableFormat.Dbf, 1 => TableFormat.Json, _ => TableFormat.Csv }, names));
        };
        cancel.Click += (_, _) => window.Close();
        return await window.ShowDialog<NewTableChoice?>(owner);
    }

    public static async Task<IReadOnlyList<DbfFieldDefinition>?> Schema(Window owner, IEnumerable<DbfFieldDefinition> source)
    {
        var fields = new ObservableCollection<FieldDraft>(source.Select(f => new FieldDraft { Name = f.Name, Type = f.Type, Length = f.Length, DecimalCount = f.DecimalCount, SourceName = f.SourceName }));
        var grid = new DataGrid { ItemsSource = fields, AutoGenerateColumns = false, CanUserResizeColumns = true, IsReadOnly = false, SelectionMode = DataGridSelectionMode.Single };
        grid.Columns.Add(new DataGridTextColumn { Header = Lang.T("Name"), Binding = new Binding("Name"), Width = new(130) });
        grid.Columns.Add(new DataGridTemplateColumn { Header = Lang.T("Type"), Width = new(125),
            CellTemplate = new FuncDataTemplate<FieldDraft>((f, _) => new TextBlock { Text = f?.Type.ToString(), Margin = new(8, 5) }),
            CellEditingTemplate = new FuncDataTemplate<FieldDraft>((f, _) =>
            {
                var combo = new ComboBox { ItemsSource = new[] { NativeDbType.Char, NativeDbType.Numeric, NativeDbType.Float, NativeDbType.Date, NativeDbType.Logical } };
                combo.Bind(SelectingItemsControl.SelectedItemProperty, new Binding("Type") { Mode = BindingMode.TwoWay });
                return combo;
            }) });
        grid.Columns.Add(new DataGridTextColumn { Header = Lang.T("Length"), Binding = new Binding("Length"), Width = new(100) });
        grid.Columns.Add(new DataGridTextColumn { Header = Lang.T("Decimals"), Binding = new Binding("DecimalCount"), Width = new(100) });
        grid.Columns.Add(new DataGridTextColumn { Header = Lang.T("Source"), Binding = new Binding("SourceName"), Width = new(140) });
        var add = new Button { Content = Lang.T("AddField") };
        var remove = new Button { Content = Lang.T("DeleteField") };
        add.Click += (_, _) => fields.Add(new() { Name = $"FIELD{fields.Count + 1}", Type = NativeDbType.Char, Length = 50 });
        remove.Click += (_, _) => { if (grid.SelectedItem is FieldDraft field) fields.Remove(field); };
        var validation = new TextBlock { Foreground = Brush.Parse("#CF5961"), TextWrapping = TextWrapping.Wrap };
        var body = new Grid { RowDefinitions = new("Auto,Auto,*,Auto"), RowSpacing = 12 };
        body.Children.Add(new TextBlock { Text = Lang.T("SchemaHelp"), TextWrapping = TextWrapping.Wrap });
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { add, remove } }; Grid.SetRow(toolbar, 1); body.Children.Add(toolbar);
        Grid.SetRow(grid, 2); body.Children.Add(grid); Grid.SetRow(validation, 3); body.Children.Add(validation);
        var buttons = Buttons(out var accept, out var cancel);
        var window = Shell(Lang.T("SchemaEdit"), body, buttons, 800, 620);
        accept.Click += (_, _) =>
        {
            if (!grid.CommitEdit()) return;
            var definitions = fields.Select(f => new DbfFieldDefinition(f.Name.Trim(), f.Type, f.Length, f.DecimalCount, string.IsNullOrWhiteSpace(f.SourceName) ? null : f.SourceName.Trim())).ToArray();
            try { TableLens.Core.Dbf.DbfDocuments.Validate(definitions); window.Close(definitions); }
            catch (InvalidDataException ex) { validation.Text = ex.Message; }
        };
        cancel.Click += (_, _) => window.Close();
        return await window.ShowDialog<IReadOnlyList<DbfFieldDefinition>?>(owner);
    }

    public static async Task ShowTable(Window owner, string title, object items, string? description = null)
    {
        var grid = new DataGrid { AutoGenerateColumns = true, IsReadOnly = true, CanUserResizeColumns = true, ItemsSource = (System.Collections.IEnumerable)items, ClipboardCopyMode = DataGridClipboardCopyMode.IncludeHeader };
        var body = new DockPanel { LastChildFill = true };
        if (description is not null) { var text = new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) }; DockPanel.SetDock(text, Dock.Top); body.Children.Add(text); }
        body.Children.Add(grid);
        var close = new Button { Content = Lang.T("Close") };
        var window = Shell(title, body, close, 1000, 620);
        close.Click += (_, _) => window.Close();
        await window.ShowDialog(owner);
    }

    public static Window Shell(string title, Control body, Control buttons, double width, double height)
    {
        var panel = new DockPanel { Margin = new(22), LastChildFill = true };
        buttons.Margin = new(0, 18, 0, 0); buttons.HorizontalAlignment = HorizontalAlignment.Right; DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons); panel.Children.Add(body);
        return new Window { Title = title, Width = width, Height = height, MinWidth = 400, MinHeight = 160, MaxHeight = 850, Content = panel, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
    }

    private static StackPanel Buttons(out Button accept, out Button cancel)
    {
        accept = new Button { Content = Lang.T("Apply"), IsDefault = true, Classes = { "primary" } };
        cancel = new Button { Content = Lang.T("Cancel"), IsCancel = true };
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { cancel, accept } };
    }
    private static TextBlock Label(string key) => new() { Text = Lang.T(key), FontWeight = FontWeight.SemiBold };
}

internal sealed record ExportChoice(TableFormat Format, bool VisibleOnly);
internal sealed record NewTableChoice(TableFormat Format, string[] Columns);
internal sealed class FieldDraft
{
    public string Name { get; set; } = "";
    public NativeDbType Type { get; set; }
    public int Length { get; set; }
    public int DecimalCount { get; set; }
    public string? SourceName { get; set; }
}
